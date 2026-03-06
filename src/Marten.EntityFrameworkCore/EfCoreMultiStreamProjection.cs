using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Storage;
using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Base class for multi-stream aggregate projections that persist the aggregate
/// through an EF Core DbContext instead of Marten's document storage.
/// The DbContext is also available in <see cref="ApplyEvent"/> for data lookups.
/// </summary>
/// <typeparam name="TDoc">The aggregate document type persisted by EF Core</typeparam>
/// <typeparam name="TId">The aggregate identity type</typeparam>
/// <typeparam name="TDbContext">The EF Core DbContext type to use</typeparam>
public abstract class EfCoreMultiStreamProjection<TDoc, TId, TDbContext>
    : MultiStreamProjection<TDoc, TId>, IValidatedProjection<StoreOptions>
    where TDoc : class where TId : notnull where TDbContext : DbContext
{
    private string? _schemaName;

    /// <summary>
    /// Optional configuration for the DbContextOptionsBuilder.
    /// Override to customize EF Core options. The Npgsql provider is already
    /// configured before this is called.
    /// </summary>
    public virtual void ConfigureDbContext(DbContextOptionsBuilder<TDbContext> builder)
    {
    }

    /// <summary>
    /// Registers this projection's aggregate type for EF Core-based storage
    /// with Marten's custom projection storage providers.
    /// </summary>
    internal void RegisterEfCoreStorage(StoreOptions options)
    {
        _schemaName = options.DatabaseSchemaName;
        var schemaName = _schemaName;
        options.CustomProjectionStorageProviders[typeof(TDoc)] = (session, tenantId) =>
        {
            var (dbContext, initialConnection) = session.Database.Create<TDbContext>(ConfigureDbContext, schemaName);

            if (session is ITransactionParticipantRegistrar registrar)
            {
                registrar.AddTransactionParticipant(
                    new DbContextTransactionParticipant<TDbContext>(dbContext, initialConnection, schemaName));
            }

            return new EfCoreProjectionStorage<TDoc, TId, TDbContext>(dbContext, tenantId);
        };
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
        // Extract the DbContext from the EfCoreProjectionStorage if available
        TDbContext? dbContext = null;
        if (identitySetter is EfCoreProjectionStorage<TDoc, TId, TDbContext> efStorage)
        {
            dbContext = efStorage.DbContext;
        }

        // Fallback: create a DbContext directly (e.g., for Live aggregation)
        if (dbContext == null)
        {
            var (ctx, initialConnection) = session.Database.Create<TDbContext>(ConfigureDbContext, _schemaName);
            dbContext = ctx;

            if (session is ITransactionParticipantRegistrar registrar)
            {
                registrar.AddTransactionParticipant(
                    new DbContextTransactionParticipant<TDbContext>(dbContext, initialConnection, _schemaName));
            }
        }

        return ApplyEventsAsync(snapshot, identity, events, session, dbContext, cancellation);
    }

    /// <summary>
    /// Override to apply events with access to both the aggregate and
    /// an EF Core DbContext. The default implementation calls
    /// <see cref="ApplyEvent"/> for each event.
    /// </summary>
    [JasperFxIgnore]
    protected virtual ValueTask<(TDoc?, ActionType)> ApplyEventsAsync(
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
    /// Override to apply a single event. Use <paramref name="dbContext"/> for EF Core data lookups.
    /// The aggregate is persisted through EF Core's DbContext, not Marten.
    /// </summary>
    [JasperFxIgnore]
    public virtual TDoc? ApplyEvent(TDoc? snapshot, TId identity, IEvent @event,
        TDbContext dbContext)
    {
        return snapshot;
    }

    /// <summary>
    /// Validates configuration specific to EF Core projections. Overrides the base
    /// <see cref="MultiStreamProjection{TDoc,TId}"/> validation which assumes Marten document storage.
    /// </summary>
    IEnumerable<string> IValidatedProjection<StoreOptions>.ValidateConfiguration(StoreOptions options)
    {
        if (options.Events.TenancyStyle == TenancyStyle.Conjoined
            && !typeof(TDoc).CanBeCastTo<ITenanted>())
        {
            yield return
                $"The EF Core projection aggregate type {typeof(TDoc).FullNameInCode()} must implement " +
                $"{nameof(ITenanted)} because the event store uses conjoined multi-tenancy. " +
                $"Add a TenantId property via the {nameof(ITenanted)} interface so tenant_id is written to the EF Core table.";
        }
    }
}
