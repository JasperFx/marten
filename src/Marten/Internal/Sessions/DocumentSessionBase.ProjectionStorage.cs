using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using Marten.Events.Archiving;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.Sessions;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2087",
    Justification = "Class-level: generic method/type argument flows reflective Type values into a DAM-annotated target. Source preserved at the registration boundary.")]
public abstract partial class DocumentSessionBase
{
    public async Task<IProjectionStorage<TDoc, TId>> FetchProjectionStorageAsync<TDoc, TId>(string tenantId,
        CancellationToken cancellationToken) where TDoc : notnull where TId : notnull
    {
        // Check for custom projection storage providers (e.g., EF Core)
        if (Options.CustomProjectionStorageProviders.TryGetValue(typeof(TDoc), out var factory))
        {
            // Ensure ExtendedSchemaObjects (e.g. EF Core entity tables) are created
            await Database.EnsureStorageExistsAsync(typeof(StorageFeatures), cancellationToken).ConfigureAwait(false);
            return (IProjectionStorage<TDoc, TId>)factory(this, tenantId);
        }

        await Database.EnsureStorageExistsAsync(typeof(TDoc), cancellationToken).ConfigureAwait(false);
        if (tenantId == TenantId || tenantId.IsEmpty()) return new ProjectionStorage<TDoc, TId>(this, StorageFor<TDoc, TId>());

        var nested = ForTenant(tenantId);

        return new ProjectionStorage<TDoc, TId>((DocumentSessionBase)nested, StorageFor<TDoc, TId>());
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2087",
    Justification = "Class-level: generic method/type argument flows reflective Type values into a DAM-annotated target. Source preserved at the registration boundary.")]
internal class ProjectionStorage<TDoc, TId>: IProjectionStorage<TDoc, TId> where TId : notnull where TDoc : notnull
{
    private readonly DocumentSessionBase _session;
    private readonly IDocumentStorage<TDoc, TId> _storage;

    public ProjectionStorage(DocumentSessionBase session, IDocumentStorage<TDoc, TId> storage)
    {
        _session = session;
        _storage = storage;
    }

    public Type IdType => typeof(TId);

    // #4685 PR 2 (proving the blocker) — when the backing session is a rebuild replay, the
    // projection table was TRUNCATEd / per-tenant-DELETEd before replay started, so every
    // write is (in principle) an INSERT. Routing through InsertProjected instead of
    // UpsertProjected/OverwriteProjected makes the batch classify as BatchFlushMode.InsertOnly
    // so the BulkWriter (binary COPY) path can pick it up. CAUTION: this is only correct when
    // each aggregate id is written exactly once across the whole rebuild, which Marten's
    // page-batched rebuild does NOT guarantee today — see the EXPERIMENTAL
    // ProjectionOptions.RebuildWithInsertOnly flag (default off, no behavior change) and the
    // Bug_4685_rebuild_insert_only_multipage proving test for why this is gated off until
    // CritterWatch#208 Phase 4 (single-flush) lands.
    private bool IsRebuild =>
        _session.ExecutionMode == JasperFx.Events.Daemon.ShardExecutionMode.Rebuild
        && _session.Options.Projections.RebuildWithInsertOnly;

    public TId Identity(TDoc document)
    {
        return _storage.Identity(document);
    }

    public string TenantId => _session.TenantId;
    public void HardDelete(TDoc snapshot)
    {
        var deletion = _storage.HardDeleteForDocument(snapshot, TenantId);
        _session.QueueOperation(deletion);
    }

    public void UnDelete(TDoc snapshot)
    {
        var deletion = new StatementOperation(_storage, new UnSoftDelete(_storage));
        var where = _storage.ByIdFilter(_storage.Identity(snapshot));
        deletion.Wheres.Add(where);
        _session.QueueOperation(deletion);
    }

    public void Store(TDoc snapshot)
    {
        // #4667 — UpsertProjected (not Upsert) so we never read or mutate
        // _session.Versions / _session.Revisions from a daemon worker. The
        // projection runtime is by-contract not session-state-aware.
        // #4685 PR 2 — rebuild replay routes to InsertProjected.
        var op = IsRebuild ? _storage.InsertProjected(snapshot, TenantId) : _storage.UpsertProjected(snapshot, TenantId);
        _session.QueueOperation(op);
    }

    public void Delete(TId identity)
    {
        var deletion = _storage.DeleteForId(identity, TenantId);
        _session.QueueOperation(deletion);
    }

    public void HardDelete(TDoc snapshot, string tenantId)
    {
        var deletion = _storage.HardDeleteForDocument(snapshot, TenantId);
        _session.QueueOperation(deletion);
    }

    public void UnDelete(TDoc snapshot, string tenantId)
    {
        var deletion = new StatementOperation(_storage, new UnSoftDelete(_storage));
        var where1 = _storage.ByIdFilter(_storage.Identity(snapshot));
        deletion.Wheres.Add(where1);

        if (_storage.TenancyStyle == TenancyStyle.Conjoined)
        {
            var tenantFilter = new WhereFragment("d.tenant_id = ?", TenantId);
            deletion.Wheres.Add(tenantFilter);
        }

        _session.QueueOperation(deletion);
    }

    public void Store(TDoc snapshot, TId id, string tenantId)
    {
        _storage.SetIdentity(snapshot, id);

        // #4667 — The ItemMap eject + IdentityMap-storage Store call below is
        // the GH-3850 fix: inline-projection-rewriting-an-immutable-aggregate
        // on an IdentitySession needs the freshly-built snapshot instance to
        // replace the stale identity-map entry so a subsequent FetchLatest on
        // the same session sees the new state. That ItemMap mutation is a race
        // source under the async daemon's parallel Block(10, ...) workers
        // (#4657), but the daemon never opts into UseIdentityMapForAggregates,
        // so we gate the identity-map maintenance on the same flag. The
        // default (false) case takes the session-state-free UpsertProjected
        // path; the opt-in (true) case preserves the GH-3850 semantics and
        // accepts the documented race risk per the #4667 Phase 3 design note
        // ("opt-in is not safe for parallel projection workers").
        if (_session.Options.EventGraph.UseIdentityMapForAggregates)
        {
            // The aggregate may already be in the identity map from a prior
            // SaveChangesAsync on the same session — for example, a
            // FetchForWriting → save → StartStream sequence. In that case
            // the projection has built a NEW snapshot instance for this save
            // and the duplicate-instance guard in IdentityMapDocumentStorage.store
            // would throw before the underlying event store can surface the
            // real conflict (ExistingStreamIdCollisionException). Evict the
            // stale entry so the new snapshot can take its place.
            _session.EjectAggregateFromIdentityMap<TDoc, TId>(id);

            // Put it in the identity map -- if necessary
            _storage.Store(_session, snapshot);
        }

        // #4685 PR 2 — rebuild replay routes to InsertProjected.
        var op = IsRebuild ? _storage.InsertProjected(snapshot, tenantId) : _storage.UpsertProjected(snapshot, tenantId);
        _session.QueueOperation(op);
    }

    public void Delete(TId identity, string tenantId)
    {
        var deletion = _storage.DeleteForId(identity, tenantId);
        _session.QueueOperation(deletion);
    }

    public async Task<IReadOnlyDictionary<TId, TDoc>> LoadManyAsync(TId[] identities, CancellationToken cancellationToken)
    {
        // #4667 Phase 2 — default (UseIdentityMapForAggregates = false) routes
        // through LoadManyProjectedAsync, which opens a fresh connection and
        // uses a tracker-free deserialization path so the async daemon's
        // parallel slice workers never touch _session.Versions /
        // _session.ItemMap / _session.ChangeTrackers. The opt-in (true) case
        // preserves the inline-projection identity-map semantics by falling
        // through to the session-aware LoadManyAsync — at the cost of the
        // race the flag's design note already documents.
        var docs = _session.Options.EventGraph.UseIdentityMapForAggregates
            ? await _storage.LoadManyAsync(identities, _session, cancellationToken).ConfigureAwait(false)
            : await _storage.LoadManyProjectedAsync(identities, _session.Database, TenantId, cancellationToken).ConfigureAwait(false);
        return docs.ToDictionary(doc => _storage.Identity(doc));
    }

    public void SetIdentity(TDoc document, TId identity)
    {
        _storage.SetIdentity(document, identity);
    }

    public void StoreProjection(TDoc aggregate, IEvent lastEvent, AggregationScope scope)
    {
        // OverwriteProjected (not Overwrite) so we never touch _session.Versions. The async
        // daemon shares a single session across parallel slice handlers (Block(10, ...) inside
        // AggregationRunner.BuildBatchAsync), and IMartenSession is documented as not
        // thread-safe. The projection path doesn't need session-level tracking anyway --
        // the revision is set explicitly from the event below and IgnoreConcurrencyViolation
        // = true makes any tracker bookkeeping moot.
        // #4685 PR 2 — rebuild replay routes to InsertProjected (still INSERT-only post-teardown);
        // the revision is set explicitly from the event below either way.
        var op = IsRebuild ? _storage.InsertProjected(aggregate, TenantId) : _storage.OverwriteProjected(aggregate, TenantId);
        if (op is IRevisionedOperation r)
        {
            r.Revision = scope == AggregationScope.SingleStream ? (int)lastEvent.Version : (int)lastEvent.Sequence;
            r.IgnoreConcurrencyViolation = true;
        }

        _session.QueueOperation(op);
    }

    public void ArchiveStream(TId sliceId, string tenantId)
    {
        var op = archiveOperationBuilderFor<TId>()(sliceId);
        op.TenantId = tenantId;

        _session.QueueOperation(op);
    }

    private static ImHashMap<Type, object> _archiveBuilders = ImHashMap<Type, object>.Empty;

    private Func<TId, ArchiveStreamOperation> archiveOperationBuilderFor<TId>()
    {
        if (_archiveBuilders.TryFind(typeof(TId), out var raw))
        {
            return (Func<TId, ArchiveStreamOperation>)raw;
        }

        Func<TId, ArchiveStreamOperation> builder = null;
        if (_session.Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            if (typeof(TId) == typeof(Guid))
            {
                builder = id => new ArchiveStreamOperation(_session.Options.EventGraph, id);
            }
            else
            {
                var valueType = ValueTypeInfo.ForType(typeof(TId));
                var unWrapper = valueType.UnWrapper<TId, Guid>();
                builder = id =>  new ArchiveStreamOperation(_session.Options.EventGraph, unWrapper(id));
            }
        }
        else
        {
            if (typeof(TId) == typeof(string))
            {
                builder = id => new ArchiveStreamOperation(_session.Options.EventGraph, id);
            }
            else
            {
                var valueType = ValueTypeInfo.ForType(typeof(TId));
                var unWrapper = valueType.UnWrapper<TId, string>();
                builder = id =>  new ArchiveStreamOperation(_session.Options.EventGraph, unWrapper(id));
            }
        }

        _archiveBuilders = _archiveBuilders.AddOrUpdate(typeof(TId), builder);
        return builder;
    }

    //TODO fix in IProjectionStorage
    public Task<TDoc?> LoadAsync(TId id, CancellationToken cancellation)
    {
        // #4667 Phase 2 — see LoadManyAsync above for rationale.
        return _session.Options.EventGraph.UseIdentityMapForAggregates
            ? _storage.LoadAsync(id, _session, cancellation)
            : _storage.LoadProjectedAsync(id, _session.Database, TenantId, cancellation);
    }
}
