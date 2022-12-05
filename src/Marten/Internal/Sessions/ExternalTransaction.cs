using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class ExternalTransaction: IConnectionLifetime
{
    private readonly SessionOptions _options;

    public ExternalTransaction(SessionOptions options)
    {
        Connection = options.Connection;
        Transaction = options.Transaction;
        _options = options;
    }

    public int CommandTimeout => _options.Timeout ?? Connection?.CommandTimeout ?? 30;

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

    public ValueTask EnsureConnectedAsync(CancellationToken token)
    {
        return new ValueTask();
    }
}
