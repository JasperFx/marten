#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class TransactionalConnection: ConnectionLifetimeBase, IAlwaysConnectedLifetime
{
    protected readonly SessionOptions _options;
    protected NpgsqlConnection? _connection;


    public TransactionalConnection(SessionOptions options)
    {
        _options = options;
        _connection = _options.Connection;

        CommandTimeout = _options.Timeout ?? _connection?.CommandTimeout ?? 30;
    }

    public NpgsqlConnection Connection
    {
        get
        {
            EnsureConnected();
            return _connection!;
        }
    }

    public int CommandTimeout { get; set; }

    public NpgsqlTransaction? Transaction { get; protected set; }

    public async ValueTask DisposeAsync()
    {
        if (Transaction != null)
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        Transaction?.SafeDispose();
        _connection?.SafeDispose();
    }

    public virtual void Apply(NpgsqlCommand command)
    {
        EnsureConnected();

        command.Connection = _connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public void Apply(NpgsqlBatch batch)
    {
        EnsureConnected();

        batch.Connection = _connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;
    }

    public virtual void BeginTransaction()
    {
        EnsureConnected();
        if (Transaction == null)
        {
            Transaction = _connection.BeginTransaction(_options.IsolationLevel);
        }
    }

    // TODO -- this should be ValueTask
    public virtual async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);

        command.Connection = _connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);

        batch.Connection = _connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;
    }

    public virtual async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);
        Transaction ??= await _connection
            .BeginTransactionAsync(_options.IsolationLevel, token).ConfigureAwait(false);
    }

    public void Commit()
    {
        if (Transaction == null)
        {
            throw new InvalidOperationException("Trying to commit a transaction that was never started");
        }

        Transaction.Commit();
        Transaction.Dispose();
        Transaction = null;

        _connection?.Close();
        _connection = null;
    }

    public async Task CommitAsync(CancellationToken token)
    {
        if (Transaction == null)
        {
            throw new InvalidOperationException("Trying to commit a transaction that was never started");
        }

        await Transaction.CommitAsync(token).ConfigureAwait(false);
        await Transaction.DisposeAsync().ConfigureAwait(false);
        Transaction = null;

        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _connection = null;
    }

    public void Rollback()
    {
        if (Transaction != null)
        {
            Transaction.Rollback();
            Transaction.Dispose();
            Transaction = null;

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
    }

    public async Task RollbackAsync(CancellationToken token)
    {
        if (Transaction != null)
        {
            await Transaction.RollbackAsync(token).ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
            Transaction = null;

            if (_connection != null)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
            }

            _connection = null;
        }
    }


    public void EnsureConnected()
    {
        if (_connection == null)
        {
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }

        if (_connection.State == ConnectionState.Closed)
        {
            _connection.Open();
        }
    }

    public async ValueTask EnsureConnectedAsync(CancellationToken token)
    {
        if (_connection == null)
        {
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }

        if (_connection.State == ConnectionState.Closed)
        {
            await _connection.OpenAsync(token).ConfigureAwait(false);
        }
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
                var batch = page.Compile();
                await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);
                await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
                await reader.CloseAsync().ConfigureAwait(false);
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
            await RollbackAsync(token).ConfigureAwait(false);
            var ex = exceptions.Single();
            ExceptionDispatchInfo.Throw(ex);
        }

        if (exceptions.Any())
        {
            await RollbackAsync(token).ConfigureAwait(false);
            throw new AggregateException(exceptions);
        }

        await CommitAsync(token).ConfigureAwait(true);
    }
}
