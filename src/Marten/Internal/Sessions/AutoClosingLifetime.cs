#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Services;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class AutoClosingLifetime: ConnectionLifetimeBase, IConnectionLifetime, ITransactionStarter
{
    private readonly SessionOptions _options;
    private readonly IMartenDatabase _database;
    private readonly ConnectionUsage _readerConnectionUsage;

    public AutoClosingLifetime(SessionOptions options, StoreOptions storeOptions)
    {
        if (options.Tenant == null)
            throw new ArgumentOutOfRangeException(nameof(options), "Tenant.Database cannot be null");

        _options = options;
        ExceptionTransforms = options.ExceptionTransforms;
        _database = options.Tenant!.Database;

        // #5269: same resolution as every other lifetime -- this one builds its own rather than being handed
        // one by SessionOptions, and it is the lifetime the default (non-sticky) write path uses.
        CommandTimeout = _options.ResolveCommandTimeout(storeOptions);
        // #5888: ReadFromPrimary keeps a read-only session off the standby, for readers like the async
        // daemon's event loader whose results must agree with state read on the primary.
        _readerConnectionUsage =
            options.Mode == CommandRunnerMode.ReadOnly && !options.ReadFromPrimary
            ? ConnectionUsage.Read
            : ConnectionUsage.ReadWrite;
    }

    public int CommandTimeout { get; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {

    }

    protected virtual Task AfterOpenedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public async Task<int> ExecuteAsync(NpgsqlCommand command,
        CancellationToken token = new())
    {
        Logger.OnBeforeExecute(command);
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        await AfterOpenedAsync(conn, token).ConfigureAwait(false);

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            Logger.LogSuccess(command);

            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(command, e);
            throw;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        Logger.OnBeforeExecute(command);

        // Do NOT use a using block here because we're returning the reader
        var conn = await openConnectionAsync(_readerConnectionUsage, token).ConfigureAwait(false);

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection, token).ConfigureAwait(false);
            Logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            handleCommandException(command, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        Logger.OnBeforeExecute(batch);

        // Do NOT use a using block here because we're returning the reader
        var conn = await openConnectionAsync(_readerConnectionUsage, token).ConfigureAwait(false);

        try
        {
            batch.Connection = conn;
            batch.Timeout = CommandTimeout;
            var reader = await batch.ExecuteReaderAsync(CommandBehavior.CloseConnection, token).ConfigureAwait(false);

            Logger.LogSuccess(batch);

            return reader;
        }
        catch (Exception)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, List<Exception> exceptions,
        CancellationToken token, IReadOnlyList<ITransactionParticipant>? participants = null)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        await AfterOpenedAsync(conn, token).ConfigureAwait(false);

        try
        {
            var tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false);

            try
            {
                foreach (var page in pages)
                {
                    var batch = page.Compile();

                    batch.Timeout = CommandTimeout;
                    batch.Connection = conn;
                    batch.Transaction = tx;

                    Logger.OnBeforeExecute(batch);
                    try
                    {
                        DbDataReader reader;
                        try
                        {
                            reader = await batch.ExecuteReaderAsync(token).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            // #5914: only the operations whose statements the reader executed translate this
                            if (page.TryTransformExecuteFailure(e, out var transformed))
                            {
                                throw transformed;
                            }

                            throw;
                        }

                        await using (reader.ConfigureAwait(false))
                        {
                            await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
                            await reader.CloseAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.LogFailure(batch, e);

                        // This lifetime executes the batch itself rather than going through
                        // handleCommandException, so the batch has to be recorded here or the SQL is
                        // gone by the time the outer catch builds a MartenCommandException.
                        e.RecordNpgsqlBatch(batch);
                        throw;
                    }

                    Logger.LogSuccess(batch);
                }
            }
            catch (Exception e)
            {
                // #5688 / #6074: a failed rollback -- a broken connection took the transaction with it -- must not
                // replace the failure that caused it
                await tx.RollbackAfterFailureAsync(Logger).ConfigureAwait(false);

                Logger.LogFailure(new NpgsqlCommand(), e);
                transformAndThrow(pages, e);
            }

            if (exceptions.Count == 1)
            {
                await tx.RollbackAfterFailureAsync(Logger).ConfigureAwait(false);

                var ex = exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (exceptions.Any())
            {
                await tx.RollbackAfterFailureAsync(Logger).ConfigureAwait(false);

                throw new AggregateException(exceptions);
            }

            if (participants is { Count: > 0 })
            {
                foreach (var participant in participants)
                {
                    await participant.BeforeCommitAsync(conn, tx, token).ConfigureAwait(false);
                }
            }

            await tx.CommitAsync(token).ConfigureAwait(false);

            // #5603: only now is the participants' work durable. A participant holding provisional
            // state -- the EF Core change tracker -- accepts it here, never in BeforeCommitAsync,
            // because that method runs inside the retried block and may have run more than once.

            if (participants is { Count: > 0 })
            {
                foreach (var participant in participants)
                {
                    await participant.AfterCommitAsync(token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <remarks>
    ///     #5881: the new lifetime belongs to nobody until it is returned. The session only takes ownership of it
    ///     from the return value, so if beginning the transaction fails -- most often a token cancelled after the
    ///     connection opened -- this method is the only place left that can give the connection back.
    /// </remarks>
    public async Task<IAlwaysConnectedLifetime> StartAsync(CancellationToken token)
    {
        var transaction = CreateStickyConnection();
        try
        {
            await transaction.BeginTransactionAsync(token).ConfigureAwait(false);
            return transaction;
        }
        catch (Exception)
        {
            await transaction.DisposeAfterFailureAsync(Logger).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     #5881. Opens a connection for one operation and runs <see cref="AfterOpenedAsync" /> on it, closing it
    ///     again if either fails. The callers that hand back a reader cannot use a <c>using</c> block, and the row
    ///     level security subclass runs a command in <see cref="AfterOpenedAsync" /> -- a command that a cancelled
    ///     token makes throw after the connection is already open.
    /// </summary>
    private async Task<NpgsqlConnection> openConnectionAsync(ConnectionUsage usage, CancellationToken token)
    {
        var conn = _database.CreateConnection(usage);
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            await AfterOpenedAsync(conn, token).ConfigureAwait(false);
            return conn;
        }
        catch (Exception)
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    protected virtual TransactionalConnection CreateStickyConnection()
    {
        return _options.Mode == CommandRunnerMode.ReadOnly
            ? new ReadOnlyTransactionalConnection(_options) { Logger = Logger, CommandTimeout = CommandTimeout }
            : new TransactionalConnection(_options) { Logger = Logger, CommandTimeout = CommandTimeout };
    }
}
