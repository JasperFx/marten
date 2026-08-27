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

    public DbContextTransactionParticipant(TDbContext dbContext, NpgsqlConnection initialConnection,
        string? schemaName = null)
    {
        DbContext = dbContext;
        _initialConnection = initialConnection;
        _schemaName = schemaName;
    }

    public TDbContext DbContext { get; }

    public async Task BeforeCommitAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, CancellationToken token)
    {
        // A null transaction means the session is in an ambient TransactionScope and the connection
        // carries the enlistment. Assigning null to DbCommand.Transaction is correct there, and
        // UseTransactionAsync(null) is required rather than merely tolerated: EF Core throws if it
        // is handed a transaction while already operating inside a TransactionScope.
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

    public ValueTask DisposeAsync() => ReleaseAsync();

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _initialConnection.Dispose();
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
