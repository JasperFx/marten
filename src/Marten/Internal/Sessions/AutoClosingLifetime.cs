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
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class AutoClosingLifetime: ConnectionLifetimeBase, IConnectionLifetime, ITransactionStarter
{
    private readonly SessionOptions _options;
    private readonly IMartenDatabase _database;

    public AutoClosingLifetime(SessionOptions options)
    {
        if (options.Tenant == null)
            throw new ArgumentOutOfRangeException(nameof(options), "Tenant.Database cannot be null");

        _options = options;
        _database = options.Tenant!.Database;

        CommandTimeout = _options.Timeout ?? 30;
    }

    public int CommandTimeout { get; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {

    }


    public int Execute(NpgsqlCommand cmd, IMartenSessionLogger logger)
    {
        logger.OnBeforeExecute(cmd);
        using var conn = _database.CreateConnection();
        conn.Open();
        try
        {
            cmd.Connection = conn;
            cmd.CommandTimeout = CommandTimeout;
            var returnValue = cmd.ExecuteNonQuery();

            logger.LogSuccess(cmd);
            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(logger, cmd, e);
            throw;
        }
        finally
        {
            conn.Close();
        }
    }

    public async Task<int> ExecuteAsync(NpgsqlCommand command, IMartenSessionLogger logger,
        CancellationToken token = new CancellationToken())
    {
        logger.OnBeforeExecute(command);
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            logger.LogSuccess(command);

            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(logger, command, e);
            throw;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command, IMartenSessionLogger logger)
    {
        logger.OnBeforeExecute(command);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection();
        conn.Open();

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = command.ExecuteReader(CommandBehavior.CloseConnection);
            logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
            conn.Close();
            handleCommandException(logger, command, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, IMartenSessionLogger logger, CancellationToken token = default)
    {
        logger.OnBeforeExecute(command);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection, token).ConfigureAwait(false);
            logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            handleCommandException(logger, command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch, IMartenSessionLogger logger)
    {
        logger.OnBeforeExecute(batch);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection();
        conn.Open();

        try
        {
            batch.Connection = conn;
            batch.Timeout = CommandTimeout;
            var reader = batch.ExecuteReader(CommandBehavior.CloseConnection);
            logger.LogSuccess(batch);
            return reader;
        }
        catch (Exception)
        {
            conn.Close();
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, IMartenSessionLogger logger, CancellationToken token = default)
    {
        logger.OnBeforeExecute(batch);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            batch.Connection = conn;
            batch.Timeout = CommandTimeout;
            var reader = await batch.ExecuteReaderAsync(CommandBehavior.CloseConnection, token).ConfigureAwait(false);

            logger.LogSuccess(batch);

            return reader;
        }
        catch (Exception)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, IMartenSessionLogger logger, List<Exception> exceptions)
    {
        using var conn = _database.CreateConnection();
        conn.Open();

        try
        {
            var tx = conn.BeginTransaction();

            try
            {
                foreach (var page in pages)
                {
                    var batch = page.Compile();
                    batch.Timeout = CommandTimeout;
                    batch.Connection = conn;
                    batch.Transaction = tx;

                    logger.OnBeforeExecute(batch);
                    try
                    {
                        using var reader = batch.ExecuteReader();
                        page.ApplyCallbacks(reader, exceptions);
                        reader.Close();
                    }
                    catch (Exception e)
                    {
                        logger.LogFailure(batch, e);
                        throw;
                    }

                    logger.LogSuccess(batch);
                }
            }
            catch (Exception e)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    logger.LogFailure(rollbackEx, "Error trying to rollback an exception");
                    throw;
                }

                logger.LogFailure(new NpgsqlCommand(), e);
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>()
                    .Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (exceptions.Count == 1)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception e)
                {
                    logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                var ex = exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (exceptions.Any())
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception e)
                {
                    logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                throw new AggregateException(exceptions);
            }

            tx.Commit();
        }
        finally
        {
            conn.Close();
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, IMartenSessionLogger logger, List<Exception> exceptions, CancellationToken token)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

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

                    logger.OnBeforeExecute(batch);
                    try
                    {
                        await using var reader = batch.ExecuteReader();
                        await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
                        await reader.CloseAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        logger.LogFailure(batch, e);
                        throw;
                    }

                    logger.LogSuccess(batch);
                }
            }
            catch (Exception e)
            {
                try
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    logger.LogFailure(rollbackEx, "Error trying to rollback an exception");
                    throw;
                }

                logger.LogFailure(new NpgsqlCommand(), e);
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>()
                    .Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (exceptions.Count == 1)
            {
                try
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                var ex = exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (exceptions.Any())
            {
                try
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                throw new AggregateException(exceptions);
            }

            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public IAlwaysConnectedLifetime Start()
    {
        var transaction = _options.Mode == CommandRunnerMode.ReadOnly
            ? new ReadOnlyTransactionalConnection(_options)
            : new TransactionalConnection(_options);
        transaction.BeginTransaction();

        return transaction;
    }

    public async Task<IAlwaysConnectedLifetime> StartAsync(CancellationToken token)
    {
        var transaction = _options.Mode == CommandRunnerMode.ReadOnly
            ? new ReadOnlyTransactionalConnection(_options)
            : new TransactionalConnection(_options);

        await transaction.BeginTransactionAsync(token).ConfigureAwait(false);

        return transaction;
    }
}
