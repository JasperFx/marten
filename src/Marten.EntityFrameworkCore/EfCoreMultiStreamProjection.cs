using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using Marten.Events.Projections;
using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Base class for multi-stream aggregate projections that use both Marten
/// document storage and an EF Core DbContext. The Marten aggregate is still
/// managed by Marten; the DbContext allows writing additional entities in
/// the same transaction.
/// </summary>
/// <typeparam name="TDoc">The aggregate document type managed by Marten</typeparam>
/// <typeparam name="TId">The aggregate identity type</typeparam>
/// <typeparam name="TDbContext">The EF Core DbContext type to use</typeparam>
public abstract class EfCoreMultiStreamProjection<TDoc, TId, TDbContext>
    : MultiStreamProjection<TDoc, TId>
    where TDoc : notnull where TId : notnull where TDbContext : DbContext
{
    /// <summary>
    /// Optional configuration for the DbContextOptionsBuilder.
    /// Override to customize EF Core options. The Npgsql provider is already
    /// configured before this is called.
    /// </summary>
    protected virtual void ConfigureDbContext(DbContextOptionsBuilder<TDbContext> builder)
    {
    }

    [JasperFxIgnore]
    public override ValueTask<(TDoc?, ActionType)> DetermineActionAsync(
        IQuerySession session,
        TDoc? snapshot,
        TId identity,
        IIdentitySetter<TDoc, TId> identitySetter,
        IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        var (dbContext, initialConnection) = EfCoreDbContextFactory.Create<TDbContext>(session.Database, ConfigureDbContext);

        if (session is ITransactionParticipantRegistrar registrar)
        {
            registrar.AddTransactionParticipant(
                new DbContextTransactionParticipant<TDbContext>(dbContext, initialConnection));
        }

        return DetermineActionAsync(snapshot, identity, events, session, dbContext, cancellation);
    }

    /// <summary>
    /// Override to apply events with access to both the Marten aggregate and
    /// an EF Core DbContext. The default implementation calls
    /// <see cref="ApplyEvent"/> for each event.
    /// </summary>
    protected virtual ValueTask<(TDoc?, ActionType)> DetermineActionAsync(
        TDoc? snapshot, TId identity, IReadOnlyList<IEvent> events,
        IQuerySession session, TDbContext dbContext, CancellationToken token)
    {
        foreach (var @event in events)
        {
            snapshot = ApplyEvent(snapshot, identity, @event, dbContext);
        }

        return new ValueTask<(TDoc?, ActionType)>(
            (snapshot, snapshot == null ? ActionType.Delete : ActionType.Store));
    }

    /// <summary>
    /// Override to apply a single event. Use <paramref name="dbContext"/> for EF Core writes.
    /// The Marten aggregate (snapshot) is still managed by Marten.
    /// </summary>
    [JasperFxIgnore]
    public virtual TDoc? ApplyEvent(TDoc? snapshot, TId identity, IEvent @event,
        TDbContext dbContext)
    {
        return snapshot;
    }
}
