using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Wraps a DbContext so that it can participate in Marten's database transaction.
/// When <see cref="BeforeCommitAsync"/> is called, the DbContext is enlisted in
/// the provided connection and transaction, then its tracked changes are flushed.
/// The initial placeholder connection (used only for provider registration) is
/// disposed after being swapped out.
///
/// <para>
/// #5228: the placeholder connection used to be released ONLY on the success path, at the end of
/// <see cref="BeforeCommitAsync"/>. Every route that never reached that line leaked it — a
/// projection that threw while applying, an optimistic concurrency failure on
/// <c>SaveChangesAsync</c>, or a throw from inside <c>BeforeCommitAsync</c> itself. Worse, the
/// participant is created when the projection's storage is built, which for an inline
/// multi-stream projection happens for EVERY <c>SaveChangesAsync</c> whose events reach the
/// projection at all — including the very common case where the grouper returns nothing and the
/// projector never runs. So a workload that merely has an EF Core inline projection registered
/// leaked a pooled connection per failed save.
/// </para>
///
/// <para>
/// Release is now owned by disposal rather than by the commit path, so it happens exactly once
/// however the save ends. <see cref="BeforeCommitAsync"/> still releases eagerly on success so a
/// long-lived session does not hold connections it has finished with.
/// </para>
/// </summary>
internal class DbContextTransactionParticipant<TDbContext>: ITransactionParticipant, IAsyncDisposable, IDisposable
    where TDbContext : DbContext
{
    private readonly NpgsqlConnection _initialConnection;
    private readonly string? _schemaName;
    private bool _released;
    private bool _disposed;

    public DbContextTransactionParticipant(TDbContext dbContext, NpgsqlConnection initialConnection,
        string? schemaName = null)
    {
        DbContext = dbContext;
        _initialConnection = initialConnection;
        _schemaName = schemaName;
    }

    public TDbContext DbContext { get; }

    public async Task BeforeCommitAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken token)
    {
        // Set search_path on Marten's real connection so EF Core targets the right schema
        if (!string.IsNullOrEmpty(_schemaName))
        {
            await using var setSchema = connection.CreateCommand();
            setSchema.CommandText = $"SET search_path TO {_schemaName}";
            setSchema.Transaction = transaction;
            await setSchema.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        // Swap to Marten's real connection and transaction
        DbContext.Database.SetDbConnection(connection);
        await DbContext.Database.UseTransactionAsync(transaction, token).ConfigureAwait(false);

        // Flush all tracked changes into the same transaction
        await DbContext.SaveChangesAsync(token).ConfigureAwait(false);

        // The placeholder has been swapped out and is no longer referenced by the DbContext, so
        // release it now rather than waiting for the session to be disposed. Disposal is
        // idempotent, so the session's own teardown pass is a no-op after this.
        await ReleaseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// #5457: the DbContext is disposed here, and only here. It is created once per tenant per
    /// batch by the EF Core projection's storage factory, and nothing in Marten, the daemon or
    /// JasperFx ever disposed it -- <c>IProjectionStorage&lt;,&gt;</c> declares no disposal contract at
    /// all, so the storage that owns the context has nothing to hook. The participant is the one
    /// object in this graph whose lifetime already matches the context's: the daemon drains
    /// <c>ProjectionUpdateBatch._transactionParticipants</c> in its <c>DisposeAsync</c>, on the
    /// success path and the failure path alike, exactly once per batch.
    ///
    /// <para>
    /// Deliberately NOT released eagerly at the end of <see cref="BeforeCommitAsync"/> the way the
    /// placeholder connection is. The connection is provably finished with at that point -- it has
    /// just been swapped out of the context -- whereas the context itself is still reachable by an
    /// inline projection whose session has not finished, so tearing it down there would be a
    /// behaviour change rather than a leak fix.
    /// </para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await ReleaseAsync().ConfigureAwait(false);
        await DbContext.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_released)
        {
            _released = true;
            _initialConnection.Dispose();
        }

        DbContext.Dispose();
    }

    private async ValueTask ReleaseAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        await _initialConnection.DisposeAsync().ConfigureAwait(false);
    }
}
