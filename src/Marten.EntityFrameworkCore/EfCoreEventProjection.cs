using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Projections;
using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Base class for event projections that use both Marten document operations
/// and an EF Core DbContext. Both sets of writes are committed atomically
/// in the same database transaction.
/// </summary>
/// <typeparam name="TDbContext">The EF Core DbContext type to use</typeparam>
public abstract class EfCoreEventProjection<TDbContext>: IProjection
    where TDbContext : DbContext
{
    /// <summary>
    /// Optional configuration for the DbContextOptionsBuilder.
    /// Override to customize EF Core options (e.g., model configuration).
    /// The Npgsql provider is already configured before this is called.
    /// </summary>
    protected virtual void ConfigureDbContext(DbContextOptionsBuilder<TDbContext> builder)
    {
    }

    public async Task ApplyAsync(IDocumentOperations operations,
        IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        // Create a DbContext with a connection object from Marten's database.
        // The actual connection will be swapped in during BeforeCommitAsync.
        var (dbContext, initialConnection) = EfCoreDbContextFactory.Create<TDbContext>(operations.Database, ConfigureDbContext);
        var ops = new EfCoreOperations<TDbContext>(operations, dbContext);

        foreach (var @event in events)
        {
            await ProjectAsync(@event, ops.Marten, ops.DbContext, cancellation).ConfigureAwait(false);
        }

        if (operations is ITransactionParticipantRegistrar registrar)
        {
            registrar.AddTransactionParticipant(
                new DbContextTransactionParticipant<TDbContext>(dbContext, initialConnection));
        }
    }

    /// <summary>
    /// Override to handle each event. Use <c>operations.DbContext</c> for EF Core writes
    /// and <c>operations.Marten</c> for Marten document writes.
    /// </summary>
    public abstract Task ProjectAsync(IEvent @event,
        IDocumentOperations operations, TDbContext dbContext, CancellationToken token);
}
