#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class MartenControlledConnectionTransaction: IConnectionLifetime
{
    protected readonly SessionOptions _options;
    private readonly IRetryPolicy _retryPolicy;


    public MartenControlledConnectionTransaction(SessionOptions options, StoreOptions storeOptions)
    {
        _options = options;
        Connection = _options.Connection;
        _retryPolicy = storeOptions.RetryPolicy();
    }

    public int CommandTimeout => _options.Timeout ?? Connection?.CommandTimeout ?? 30;
    public NpgsqlTransaction? Transaction { get; protected set; }

    public async ValueTask DisposeAsync()
    {
        if (Transaction != null)
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }

        if (Connection != null)
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        Transaction?.SafeDispose();
        Connection?.SafeDispose();
    }

    public virtual void Apply(NpgsqlCommand command)
    {
        EnsureConnected();

        command.Connection = Connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public void Apply(NpgsqlBatch batch)
    {
        EnsureConnected();

        batch.Connection = Connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;
    }

    public virtual void BeginTransaction()
    {
        EnsureConnected();
        if (Transaction == null)
        {
            Transaction = Connection.BeginTransaction(_options.IsolationLevel);
        }
    }

    // TODO -- this should be ValueTask
    public virtual async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);

        command.Connection = Connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);

        batch.Connection = Connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;
    }

    public virtual async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);
        Transaction ??= await Connection
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

        Connection?.Close();
        Connection = null;
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

        if (Connection != null)
        {
            await Connection.CloseAsync().ConfigureAwait(false);
            await Connection.DisposeAsync().ConfigureAwait(false);
        }

        Connection = null;
    }

    public void Rollback()
    {
        if (Transaction != null)
        {
            Transaction.Rollback();
            Transaction.Dispose();
            Transaction = null;

            Connection?.Close();
            Connection?.Dispose();
            Connection = null;
        }
    }

    public async Task RollbackAsync(CancellationToken token)
    {
        if (Transaction != null)
        {
            await Transaction.RollbackAsync(token).ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
            Transaction = null;

            if (Connection != null)
            {
                await Connection.CloseAsync().ConfigureAwait(false);
                await Connection.DisposeAsync().ConfigureAwait(false);
            }

            Connection = null;
        }
    }

    public NpgsqlConnection? Connection { get; protected set; }

    public void EnsureConnected()
    {
        if (Connection == null)
        {
#pragma warning disable CS8602
            Connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }

        if (Connection.State == ConnectionState.Closed)
        {
            _retryPolicy.Execute(() => Connection.Open());
        }
    }

    public async ValueTask EnsureConnectedAsync(CancellationToken token)
    {
        if (Connection == null)
        {
#pragma warning disable CS8602
            Connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }

        if (Connection.State == ConnectionState.Closed)
        {
            await _retryPolicy.ExecuteAsync(() => Connection.OpenAsync(token), token).ConfigureAwait(false);
        }
    }
}
