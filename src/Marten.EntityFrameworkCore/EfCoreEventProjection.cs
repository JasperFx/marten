using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Storage;
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
        // Ensure EF Core entity tables exist (e.g., after schema was dropped by Clean)
        await operations.Database.EnsureStorageExistsAsync(typeof(StorageFeatures), cancellation)
            .ConfigureAwait(false);

        // Create a DbContext with a connection object from Marten's database.
        // The actual connection will be swapped in during BeforeCommitAsync.
        var schemaName = (operations as QuerySession)?.Options.DatabaseSchemaName;
        var (dbContext, initialConnection) = operations.Database.Create<TDbContext>(ConfigureDbContext, schemaName);
        var ops = new EfCoreOperations<TDbContext>(operations, dbContext);

        foreach (var @event in events)
        {
            await ProjectAsync(@event, ops.DbContext, ops.Marten, cancellation).ConfigureAwait(false);
        }

        if (operations is ITransactionParticipantRegistrar registrar)
        {
            registrar.AddTransactionParticipant(
                new DbContextTransactionParticipant<TDbContext>(dbContext, initialConnection, schemaName));
        }
    }

    /// <summary>
    /// Override to handle each event. Use <c>operations.DbContext</c> for EF Core writes
    /// and <c>operations.Marten</c> for Marten document writes.
    /// </summary>
    protected abstract Task ProjectAsync(IEvent @event,
        TDbContext dbContext,
        IDocumentOperations operations, CancellationToken token);
}
