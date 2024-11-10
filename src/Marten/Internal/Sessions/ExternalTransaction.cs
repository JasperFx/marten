using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Services;
using Npgsql;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Internal.Sessions;

internal class ExternalTransaction: ConnectionLifetimeBase, IAlwaysConnectedLifetime
{
    private readonly SessionOptions _options;

    public ExternalTransaction(SessionOptions options)
    {
        if (options.Connection == null || options.Transaction == null)
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                "Neither the connection nor the transaction can be null in this usage");
        }

        Connection = options.Connection!;
        Transaction = options.Transaction;
        _options = options;

        CommandTimeout = _options.Timeout ?? Connection?.CommandTimeout ?? 30;
    }

    public int CommandTimeout { get; set; }

    public NpgsqlTransaction Transaction { get; }

    public ValueTask DisposeAsync()
    {
        return new ValueTask();
    }

    public void Dispose()
    {
        // Nothing
    }

    public void Apply(NpgsqlCommand command)
    {
        command.Connection = Connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        command.Connection = Connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;

        return Task.CompletedTask;
    }

    public void Apply(NpgsqlBatch batch)
    {
        batch.Connection = Connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;
    }

    public Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        batch.Connection = Connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;

        return Task.CompletedTask;
    }

    public void Commit()
    {
        if (_options.OwnsTransactionLifecycle)
        {
            Transaction.Commit();
        }
    }

    public async Task CommitAsync(CancellationToken token)
    {
        if (_options.OwnsTransactionLifecycle)
        {
            await Transaction.CommitAsync(token).ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Rollback()
    {
        if (_options.OwnsTransactionLifecycle)
        {
            Transaction.Rollback();
        }
    }

    public Task RollbackAsync(CancellationToken token)
    {
        if (_options.OwnsTransactionLifecycle)
        {
            return Transaction.RollbackAsync(token);
        }

        return Task.CompletedTask;
    }

    public NpgsqlConnection Connection { get; }

    public void BeginTransaction()
    {
        // Nothing
    }

    public ValueTask BeginTransactionAsync(CancellationToken token)
    {
        return new ValueTask();
    }

    public void EnsureConnected()
    {
        // Nothing
    }

    public int Execute(NpgsqlCommand cmd)
    {
        Apply(cmd);

        try
        {
            var returnValue = cmd.ExecuteNonQuery();
            Logger.LogSuccess(cmd);

            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(cmd, e);
            throw;
        }
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
            handleCommandException(command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        Apply(command);

        try
        {
            var returnValue = command.ExecuteReader();
            Logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
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
            handleCommandException(command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        Apply(batch);

        try
        {
            var reader = batch.ExecuteReader();
            Logger.LogSuccess(batch);
            return reader;
        }
        catch (Exception e)
        {
            handleCommandException(batch, e);
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
            handleCommandException(batch, e);
            throw;
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages,
        List<Exception> exceptions, CancellationToken token)
    {
        try
        {
            await BeginTransactionAsync(token).ConfigureAwait(false);
            foreach (var page in pages)
            {
                var batch = page.Builder.As<BatchBuilder>().Compile();
                await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);
                await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            await RollbackAsync(token).ConfigureAwait(false);
            Logger.LogFailure(new NpgsqlCommand(), e);
            pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
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

        await CommitAsync(token).ConfigureAwait(true);
    }

}


