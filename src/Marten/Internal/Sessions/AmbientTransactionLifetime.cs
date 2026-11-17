#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class AmbientTransactionLifetime: ConnectionLifetimeBase, IAlwaysConnectedLifetime
{
    private readonly SessionOptions _options;
    private NpgsqlConnection? _connection;

    public AmbientTransactionLifetime(SessionOptions options)
    {
        _options = options;
        ExceptionTransforms = options.ExceptionTransforms;

        _connection = options.Connection;

        if (options.Connection != null && options.Connection.State != ConnectionState.Closed)
        {
            OwnsConnection = false;
        }
        else
        {
            OwnsConnection = true;
        }

        CommandTimeout = _options.Timeout ?? _connection?.CommandTimeout ?? StoreOptions.DefaultTimeout;
    }

    /// <summary>
    ///     The current connection, without opening one. Null until the lifetime has connected; use
    ///     <see cref="OpenConnectionAsync" /> to connect first.
    /// </summary>
    public NpgsqlConnection? Connection => _connection;

    public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken token)
    {
        await BeginTransactionAsync(token).ConfigureAwait(false);
        return _connection!;
    }

    public bool OwnsConnection { get; }

    public int CommandTimeout { get; set; }




    /// <summary>
    ///     #5583: the ambient System.Transactions scope owns the transaction, so a failure inside it
    ///     outlives the call and Marten must not resolve it. Recording the cause only changes what the NEXT
    ///     use of the session reports.
    /// </summary>
    protected internal override bool HasSurvivingTransaction => true;

    /// <remarks>
    ///     #5881: as <see cref="TransactionalConnection.DisposeAsync" />, the connection is disposed whatever happens
    ///     before it.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_connection is { State: ConnectionState.Open })
            {
                await BeforeCloseAsync(_connection, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (OwnsConnection)
        {
            // #5861: the RLS lifetime's one synchronous tenant reset, for a connection this lifetime opened itself
            if (_connection is { State: ConnectionState.Open } && _options.Connection == null)
            {
                resetBeforeSynchronousRelease(_connection);
            }

            _connection?.Close();
            _connection?.Dispose();
        }
    }

    public async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await BeginTransactionAsync(token).ConfigureAwait(false);
        command.Connection = _connection;
        command.CommandTimeout = CommandTimeout;
        command.CommandTimeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        await BeginTransactionAsync(token).ConfigureAwait(false);
        batch.Connection = _connection;
        batch.Timeout = CommandTimeout;
    }

    public ValueTask EnsureConnectedAsync(CancellationToken token)
    {
        return BeginTransactionAsync(token);
    }

    public async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        if (_connection == null)
        {
#pragma warning disable CS8602
            var connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602

            // #5881: the lifetime takes the connection only once it is fully set up. Assigned any earlier, a
            // failure part way -- a token cancelled after the open -- left an open connection that the next
            // call took as ready, and used without the row level security tenant and outside the ambient
            // transaction.
            try
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                await AfterOpenedAsync(connection, token).ConfigureAwait(false);
                connection.EnlistTransaction(_options.DotNetTransaction);
            }
            catch (Exception)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _connection = connection;
        }
    }

    /// <summary>
    ///     #5861: the hook a synchronous <see cref="Dispose" /> calls before it releases a connection this lifetime
    ///     opened. Only the RLS lifetime uses it. It must not throw.
    /// </summary>
    protected virtual void resetBeforeSynchronousRelease(NpgsqlConnection connection)
    {
    }

    protected virtual Task AfterOpenedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    protected virtual Task BeforeCloseAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public async Task<int> ExecuteAsync(NpgsqlCommand command,
        CancellationToken token = new())
    {
        await ApplyAsync(command, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(command);

        try
        {
            var returnValue = await command.ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
            Logger.LogSuccess(command);

            return returnValue;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(command, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        await ApplyAsync(command, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(command);

        try
        {
            var reader = await command.ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            Logger.LogSuccess(command);

            return reader;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(command, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        await ApplyAsync(batch, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(batch);

        try
        {
            var reader = await batch.ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            Logger.LogSuccess(batch);

            return reader;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(batch, e);
            throw;
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages,
        List<Exception> exceptions, CancellationToken token,
        IReadOnlyList<ITransactionParticipant>? participants = null)
    {
        try
        {
            await BeginTransactionAsync(token).ConfigureAwait(false);
            foreach (var page in pages)
            {
                var batch = page.Compile();
                await ApplyAsync(batch, token).ConfigureAwait(false);
                await using var reader = await executePageReaderAsync(page, batch, token).ConfigureAwait(false);
                await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.LogFailure(new NpgsqlCommand(), e);
            transformAndThrow(pages, e);
        }

        if (exceptions.Count == 1)
        {
            var ex = exceptions.Single();
            ExceptionDispatchInfo.Throw(ex);
        }

        if (exceptions.Any())
        {
            throw new AggregateException(exceptions);
        }

    }
}
