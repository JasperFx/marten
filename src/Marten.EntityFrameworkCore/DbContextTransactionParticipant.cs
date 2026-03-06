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
    private readonly string? _schemaName;

    public DbContextTransactionParticipant(TDbContext dbContext, NpgsqlConnection initialConnection,
        string? schemaName = null)
    {
        DbContext = dbContext;
        _initialConnection = initialConnection;
        _schemaName = schemaName;
    }

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

        // Dispose the initial placeholder connection
        await _initialConnection.DisposeAsync().ConfigureAwait(false);
    }
}
