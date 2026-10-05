using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten;

/// <summary>
/// Represents a participant that can execute additional work within Marten's
/// database transaction before it is committed. This is used to allow external
/// systems (like EF Core DbContext) to flush their changes into the same
/// transaction that Marten uses for its batch operations.
/// </summary>
public interface ITransactionParticipant
{
    /// <summary>
    /// Called after Marten's batch pages have been executed but before the
    /// transaction is committed. Implementations should use the provided
    /// connection and transaction to execute any pending work.
    /// </summary>
    /// <remarks>
    /// This may be called MORE THAN ONCE for a single SaveChangesAsync. It runs inside the block
    /// Marten's write resilience pipeline retries, so a transient failure of the commit itself runs
    /// it again on a fresh connection and transaction. An implementation must therefore be able to
    /// replay its work, and must not treat its own state as durable until
    /// <see cref="AfterCommitAsync" /> says so — which is what #5603 was.
    /// </remarks>
    Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken token);

    /// <summary>
    /// Called once the transaction this participant wrote into has actually committed. This is the
    /// only point at which work done in <see cref="BeforeCommitAsync" /> is known to be durable, so
    /// it is where an implementation holding provisional state should accept it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// #5603. Without this there was nowhere to learn that the commit succeeded, and the EF Core
    /// participant accepted its change tracker inside <see cref="BeforeCommitAsync" /> instead — so a
    /// retried commit re-ran that method against a tracker that believed it had nothing left to save,
    /// and the retry committed Marten's operations without the projection's row, silently.
    /// </para>
    /// <para>
    /// Default-implemented so that adding it does not break existing implementors, binary or source.
    /// Not called at all on a lifetime where Marten does not perform the commit —
    /// <c>AmbientTransactionLifetime</c> — for the same reason it gets no
    /// <see cref="BeforeCommitAsync" />: Marten cannot report a commit it does not make.
    /// </para>
    /// </remarks>
    Task AfterCommitAsync(CancellationToken token) => Task.CompletedTask;
}
