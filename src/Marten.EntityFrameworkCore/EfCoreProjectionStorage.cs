using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// An <see cref="IProjectionStorage{TDoc,TId}"/> implementation that persists
/// aggregates through an EF Core <see cref="DbContext"/> instead of Marten's
/// document storage. Loaded entities are read from the DbContext (which queries
/// via its own connection); stored/deleted entities are tracked and flushed when
/// <see cref="DbContextTransactionParticipant{TDbContext}.BeforeCommitAsync"/>
/// swaps to Marten's connection and calls SaveChangesAsync.
/// </summary>
internal class EfCoreProjectionStorage<TDoc, TId, TDbContext> : IProjectionStorage<TDoc, TId>
    where TDoc : class where TId : notnull where TDbContext : DbContext
{
    public TDbContext DbContext { get; }
    private readonly string _tenantId;

    public EfCoreProjectionStorage(TDbContext dbContext, string tenantId)
    {
        DbContext = dbContext;
        _tenantId = tenantId;
    }

    public string TenantId => _tenantId;
    public Type IdType => typeof(TId);

    public TId Identity(TDoc document)
    {
        var entityType = DbContext.Model.FindEntityType(typeof(TDoc));
        if (entityType == null)
            throw new InvalidOperationException($"{typeof(TDoc).Name} is not mapped in {typeof(TDbContext).Name}");

        var pk = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"{typeof(TDoc).Name} has no primary key configured in {typeof(TDbContext).Name}");

        var pkValue = DbContext.Entry(document).Property(pk.Properties[0].Name).CurrentValue;
        return (TId)pkValue!;
    }

    public void SetIdentity(TDoc document, TId identity)
    {
        var entityType = DbContext.Model.FindEntityType(typeof(TDoc));
        if (entityType == null) return;

        var pk = entityType.FindPrimaryKey();
        if (pk == null) return;

        DbContext.Entry(document).Property(pk.Properties[0].Name).CurrentValue = identity;
    }

    public void Store(TDoc snapshot)
    {
        AddOrUpdate(snapshot);
    }

    public void Store(TDoc snapshot, TId id, string tenantId)
    {
        SetIdentity(snapshot, id);
        AddOrUpdate(snapshot);
    }

    public void StoreProjection(TDoc aggregate, IEvent? lastEvent, AggregationScope scope)
    {
        AddOrUpdate(aggregate);
    }

    public void Delete(TId identity)
    {
        var entity = DbContext.Find<TDoc>(identity);
        if (entity != null) DbContext.Remove(entity);
    }

    public void Delete(TId identity, string tenantId)
    {
        Delete(identity);
    }

    public void HardDelete(TDoc snapshot)
    {
        DbContext.Remove(snapshot);
    }

    public void HardDelete(TDoc snapshot, string tenantId)
    {
        DbContext.Remove(snapshot);
    }

    public void UnDelete(TDoc snapshot)
    {
        // Not applicable for EF Core storage
    }

    public void UnDelete(TDoc snapshot, string tenantId)
    {
        // Not applicable for EF Core storage
    }

    public async Task<IReadOnlyDictionary<TId, TDoc>> LoadManyAsync(TId[] identities, CancellationToken cancellationToken)
    {
        var dict = new Dictionary<TId, TDoc>();
        foreach (var id in identities)
        {
            var entity = await DbContext.FindAsync<TDoc>(new object[] { id }, cancellationToken)
                .ConfigureAwait(false);
            if (entity != null)
            {
                dict[id] = entity;
            }
        }
        return dict;
    }

    public async Task<TDoc?> LoadAsync(TId id, CancellationToken cancellation)
    {
        return await DbContext.FindAsync<TDoc>(new object?[] { id }, cancellation)
            .ConfigureAwait(false);
    }

    public void ArchiveStream(TId sliceId, string tenantId)
    {
        // Not applicable for EF Core storage
    }

    private void AddOrUpdate(TDoc entity)
    {
        var entry = DbContext.Entry(entity);
        switch (entry.State)
        {
            case EntityState.Detached:
                DbContext.Add(entity);
                break;
            case EntityState.Unchanged:
                entry.State = EntityState.Modified;
                break;
            // Already Added or Modified — no action needed
        }
    }
}
