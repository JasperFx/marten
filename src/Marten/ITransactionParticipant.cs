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
    Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken token);
}
