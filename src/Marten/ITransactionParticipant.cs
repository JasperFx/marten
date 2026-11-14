using Npgsql;

namespace Marten;

/// <summary>
/// Represents a participant that can execute additional work within Marten's
/// database transaction before it is committed. This is used to allow external
/// systems (like EF Core DbContext) to flush their changes into the same
/// transaction that Marten uses for its batch operations.
/// </summary>
/// <remarks>
/// <para>
/// The PostgreSQL closing of Weasel.Storage's shared participant contract (#5733), which Polecat and
/// Fisher close over their own connection types. <c>BeforeCommitAsync</c> and <c>AfterCommitAsync</c>
/// are declared there, with the same signatures and semantics Marten's own interface had.
/// </para>
/// <para>
/// <c>BeforeCommitAsync</c> may be called MORE THAN ONCE for a single SaveChangesAsync. It runs inside
/// the block Marten's write resilience pipeline retries, so a transient failure of the commit itself
/// runs it again on a fresh connection and transaction. An implementation must therefore be able to
/// replay its work, and must not treat its own state as durable until <c>AfterCommitAsync</c> says so
/// — which is what #5603 was. <c>AfterCommitAsync</c> is not called at all on a lifetime where Marten
/// does not perform the commit (<c>AmbientTransactionLifetime</c>).
/// </para>
/// </remarks>
public interface ITransactionParticipant: Weasel.Storage.ITransactionParticipant<NpgsqlConnection, NpgsqlTransaction>;
