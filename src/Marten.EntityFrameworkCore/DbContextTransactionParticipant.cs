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
/// </summary>
internal class DbContextTransactionParticipant<TDbContext>: ITransactionParticipant
    where TDbContext : DbContext
{
    public TDbContext DbContext { get; }
    private readonly NpgsqlConnection _initialConnection;

    public DbContextTransactionParticipant(TDbContext dbContext, NpgsqlConnection initialConnection)
    {
        DbContext = dbContext;
        _initialConnection = initialConnection;
    }

    public async Task BeforeCommitAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken token)
    {
        // Swap to Marten's real connection and transaction
        DbContext.Database.SetDbConnection(connection);
        await DbContext.Database.UseTransactionAsync(transaction, token).ConfigureAwait(false);

        // Flush all tracked changes into the same transaction
        await DbContext.SaveChangesAsync(token).ConfigureAwait(false);

        // Dispose the initial placeholder connection
        await _initialConnection.DisposeAsync().ConfigureAwait(false);
    }
}
