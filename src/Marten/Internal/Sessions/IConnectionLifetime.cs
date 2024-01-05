#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions;

[Obsolete("going to be replaced in v7")]
public interface IConnectionLifetime: IAsyncDisposable, IDisposable
{
    NpgsqlConnection? Connection { get; }
    void Apply(NpgsqlCommand command);
    Task ApplyAsync(NpgsqlCommand command, CancellationToken token);

    void Commit();
    Task CommitAsync(CancellationToken token);

    void Rollback();
    Task RollbackAsync(CancellationToken token);

    void EnsureConnected();
    ValueTask EnsureConnectedAsync(CancellationToken token);
    void BeginTransaction();
    ValueTask BeginTransactionAsync(CancellationToken token);
    void Apply(NpgsqlBatch batch);
    Task ApplyAsync(NpgsqlBatch batch, CancellationToken token);
}
