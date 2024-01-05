#nullable enable

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class AmbientTransactionLifetime: IConnectionLifetime
{
    private readonly SessionOptions _options;

    public AmbientTransactionLifetime(SessionOptions options)
    {
        _options = options;

        Connection = options.Connection;

        if (options.Connection != null && options.Connection.State != ConnectionState.Closed)
        {
            OwnsConnection = false;
        }
        else
        {
            OwnsConnection = true;
        }
    }

    public bool OwnsConnection { get; }

    public int CommandTimeout => _options.Timeout ?? Connection?.CommandTimeout ?? 30;

    public NpgsqlConnection? Connection { get; private set; }


    public ValueTask DisposeAsync()
    {
        if (Connection != null)
        {
            return Connection.DisposeAsync();
        }

        return new ValueTask();
    }

    public void Dispose()
    {
        if (OwnsConnection)
        {
            Connection?.Close();
            Connection?.Dispose();
        }
    }

    public void Apply(NpgsqlCommand command)
    {
        BeginTransaction();
        command.Connection = Connection;
        command.CommandTimeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await BeginTransactionAsync(token).ConfigureAwait(false);
        command.Connection = Connection;
        command.CommandTimeout = CommandTimeout;
    }

    public void Apply(NpgsqlBatch batch)
    {
        BeginTransaction();
        batch.Connection = Connection;
        batch.Timeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        await BeginTransactionAsync(token).ConfigureAwait(false);
        batch.Connection = Connection;
        batch.Timeout = CommandTimeout;
    }

    public void Commit()
    {
        // Nothing
    }

    public Task CommitAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public void Rollback()
    {
        // Nothing
    }

    public Task RollbackAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public void BeginTransaction()
    {
        EnsureConnected();
    }

    public void EnsureConnected()
    {
        if (Connection == null)
        {
#pragma warning disable CS8602
            Connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
            Connection.Open();
            Connection.EnlistTransaction(_options.DotNetTransaction);
        }
    }

    public ValueTask EnsureConnectedAsync(CancellationToken token)
    {
        return BeginTransactionAsync(token);
    }

    public async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        if (Connection == null)
        {
#pragma warning disable CS8602
            Connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
            await Connection.OpenAsync(token).ConfigureAwait(false);
            Connection.EnlistTransaction(_options.DotNetTransaction);
        }
    }
}
