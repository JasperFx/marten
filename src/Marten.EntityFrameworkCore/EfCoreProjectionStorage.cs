using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
/// <remarks>
/// AOT: TDoc carries the full DAM flag set required by
/// <c>DbContext.Find&lt;TEntity&gt;</c> / <c>FindAsync&lt;TEntity&gt;</c> and
/// <c>IModel.FindEntityType(Type)</c> on the EF Core API surface. The trim
/// requirement propagates to consumers — callers that close TDoc with a
/// concrete entity type satisfy it implicitly.
/// </remarks>
internal class EfCoreProjectionStorage<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces)]
    TDoc, TId, TDbContext> : IProjectionStorage<TDoc, TId>
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

    /// <summary>
    ///     #5266. One <see cref="DbContext" /> is created per tenant/batch and shared by every slice in
    ///     the range, and a <c>DbContext</c> is not thread-safe. <c>AggregationRunner</c> otherwise applies
    ///     slices through a fixed 10-wide block, so a multi-stream projection with custom grouping — where
    ///     one event fans out into many slices — has up to ten of them concurrently calling
    ///     <see cref="DbContext.Entry(object)" /> / <c>FindAsync</c> and mutating the same change tracker.
    ///     That corrupts EF Core's identity map, surfacing as an <see cref="InvalidOperationException" />
    ///     out of <c>Dictionary.TryInsert</c> or a <see cref="NullReferenceException" /> out of
    ///     <c>ChangeDetector.DetectChanges</c>.
    /// </summary>
    /// <remarks>
    ///     Locking inside this class is not a substitute, which is why the declaration lives here rather
    ///     than being worked around: a lock around each member still leaves the aggregation on one thread
    ///     mutating entities while another thread's <c>Entry()</c> runs change detection over them. The
    ///     fan-out itself has to stop, and only the runner can stop it (jasperfx#683).
    /// </remarks>
    public bool IsThreadSafe => false;

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
