using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation.Rebuilds;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Services;
using Marten.Sessions;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events.Aggregation;

/// <summary>
/// Helpful as a base class for more custom aggregation projections that are not supported
/// by the Single/MultipleStreamProjections -- or if you'd just prefer to use explicit code
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class CustomProjection<TDoc, TId>:
    ProjectionBase,
    IAggregationRuntime<TDoc, TId>,
    IProjectionSource,
    IAggregateProjection,
    IAggregateProjectionWithSideEffects<TDoc>,
    ILiveAggregator<TDoc>
{
    private IDocumentStorage<TDoc, TId> _storage;

    protected CustomProjection()
    {
        ProjectionName = GetType().NameInCode();

        if (typeof(TId) == typeof(Guid))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamId<TDoc>();
        }
        else if (typeof(TId) == typeof(string))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamKey<TDoc>();
        }
    }

    public IAggregateProjection Projection => this;

    public bool IsSingleStream()
    {
        return Slicer is ISingleStreamSlicer;
    }

    /// <summary>
    /// Use to create "side effects" when running an aggregation (single stream, custom projection, multi-stream)
    /// asynchronously in a continuous mode (i.e., not in rebuilds)
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="slice"></param>
    /// <returns></returns>
    public virtual ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<TDoc> slice)
    {
        return new ValueTask();
    }

    public IEventSlicer<TDoc, TId> Slicer { get; protected internal set; }

    void IProjection.Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
#pragma warning disable VSTHRD002
        this.As<IProjection>().ApplyAsync(operations, streams, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    async Task IProjection.ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        var slices = await Slicer.SliceInlineActions(operations, streams).ConfigureAwait(false);

        var martenSession = (DocumentSessionBase)operations;

        await martenSession.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var storage = (IDocumentStorage<TDoc, TId>)martenSession.StorageFor<TDoc>();
        foreach (var slice in slices)
        {
            var tenantedSession = martenSession.UseTenancyBasedOnSliceAndStorage(storage, slice);

            // do not load if sliced by stream and the stream does not yet exist
            if (Slicer is not ISingleStreamSlicer || slice.ActionType != StreamActionType.Start)
            {
                slice.Aggregate = await storage.LoadAsync(slice.Id, tenantedSession, cancellation).ConfigureAwait(false);
            }
            await ApplyChangesAsync(tenantedSession, slice, cancellation).ConfigureAwait(false);
        }
    }

    async ValueTask<EventRangeGroup> IAggregationRuntime.GroupEvents(DocumentStore store, IMartenDatabase database,
        EventRange range,
        CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession(SessionOptions.ForDatabase(database));
        var groups = await Slicer.SliceAsyncEvents(session, range.Events).ConfigureAwait(false);

        return new TenantSliceRange<TDoc, TId>(store, this, range, groups, cancellationToken);
    }

    public bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor executor)
    {
        if (Slicer is ISingleStreamSlicer)
        {
            executor = new SingleStreamRebuilder<TDoc, TId>(store, database, this);
            return true;
        }

        executor = default;
        return false;
    }

    /// <summary>
    ///     Apply any document changes based on the incoming slice of events to the underlying aggregate document
    /// </summary>
    /// <param name="session"></param>
    /// <param name="slice"></param>
    /// <param name="cancellation"></param>
    /// <param name="lifecycle"></param>
    /// <returns></returns>
    public virtual async ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<TDoc, TId> slice,
        CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {
        if (!slice.Events().Any()) return;

        var snapshot = slice.Aggregate;
        snapshot = await BuildAsync(session, snapshot, slice.Events()).ConfigureAwait(false);
        ApplyMetadata(snapshot, slice.Events().Last());

        session.StorageFor<TDoc, TId>().SetIdentity(snapshot, slice.Id);

        slice.Aggregate = snapshot;
        session.Store(snapshot);

        if (Slicer is ISingleStreamSlicer<TId> singleStreamSlicer && slice.Events().OfType<IEvent<Archived>>().Any())
        {
            singleStreamSlicer.ArchiveStream(session, slice.Id);
        }
    }

    /// <summary>
    /// Override if the aggregation always updates the aggregate from new events, but may
    /// require data lookup to update the snapshot
    /// </summary>
    /// <param name="session"></param>
    /// <param name="snapshot"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    public virtual ValueTask<TDoc> BuildAsync(IQuerySession session, TDoc? snapshot, IReadOnlyList<IEvent> events)
    {
        return new ValueTask<TDoc>(Apply(snapshot, events));
    }

    /// <summary>
    /// Override if the aggregation always updates the aggregate from new events and you
    /// don't need to do any other kind of data lookup. Simplest possible way to use this
    /// </summary>
    /// <param name="snapshot"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    public virtual TDoc Apply(TDoc? snapshot, IReadOnlyList<IEvent> events)
    {
        throw new NotImplementedException("Did you forget to implement this method?");
    }

    public IAggregateVersioning Versioning { get; set; }

    /// <summary>
    ///     Override to give Marten "hints" about whether the aggregate is all new based on the incoming
    ///     event slice. The default implementation is always false.
    /// </summary>
    /// <param name="slice"></param>
    /// <returns></returns>
    public virtual bool IsNew(EventSlice<TDoc, TId> slice)
    {
        return false;
    }

    IDocumentStorage<TDoc, TId> IAggregationRuntime<TDoc, TId>.Storage => _storage;


    /// <summary>
    ///     Must be overridden to use as an async projection. Takes a range of events, and sorts them
    ///     into an EventSlice for each detected aggregate document
    /// </summary>
    /// <param name="store"></param>
    /// <param name="database"></param>
    /// <param name="range"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    public async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> GroupEventRange(DocumentStore store,
        IMartenDatabase database,
        EventRange range, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession(SessionOptions.ForDatabase(database));
        return await Slicer.SliceAsyncEvents(session, range.Events).ConfigureAwait(false);
    }

    Type IReadOnlyProjectionData.ProjectionType => GetType();

    IEnumerable<Type> IProjectionSource.PublishedTypes()
    {
        yield return typeof(TDoc);
    }

    IReadOnlyList<AsyncProjectionShard> IProjectionSource.AsyncProjectionShards(DocumentStore store)
    {
        readDocumentStorage(store);

        return new List<AsyncProjectionShard> { new(this)
        {
            IncludeArchivedEvents = IncludeArchivedEvents,
            EventTypes = IncludedEventTypes,
            StreamType = StreamType
        } };
    }

    async ValueTask<EventRangeGroup> IProjectionSource.GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase,
        EventRange range,
        CancellationToken cancellationToken)
    {
        var groups = await GroupEventRange(store, daemonDatabase, range, cancellationToken).ConfigureAwait(false);

        return new TenantSliceRange<TDoc, TId>(store, this, range, groups, cancellationToken);
    }

    IProjection IProjectionSource.Build(DocumentStore store)
    {
        readDocumentStorage(store);
        return this;
    }

    internal override void AssembleAndAssertValidity()
    {
        if (Slicer == null)
        {
            throw new InvalidProjectionException(
                $"Projection {GetType().FullNameInCode()} does not have a configured event slicer.");
        }

        if (Slicer is EventSlicer<TDoc, TId> slicer && !slicer.HasAnyRules())
        {
            throw new InvalidProjectionException(
                $"Projection {GetType().FullNameInCode()} has incomplete event slicer configuration.");
        }
    }

    public void UseCustomSlicer(IEventSlicer<TDoc, TId> custom)
    {
        Slicer = custom;
    }

    private void readDocumentStorage(DocumentStore store)
    {
        var storage = store.Options.Providers.StorageFor<TDoc>();
        _storage = storage.Lightweight as IDocumentStorage<TDoc, TId>;
        if (_storage == null)
        {
            throw new InvalidOperationException(
                $"Document type {typeof(TDoc).FullNameInCode()} has identity type {storage.QueryOnly.IdType.NameInCode()}, but projection {GetType().FullNameInCode()} is defined with id type {typeof(TId).NameInCode()}");
        }
    }

    /// <summary>
    ///     Configure event aggregation "slicing" using Marten's default, configurable event
    ///     slicer
    /// </summary>
    /// <param name="configure"></param>
    public void AggregateEvents(Action<EventSlicer<TDoc, TId>> configure)
    {
        var slicer = new EventSlicer<TDoc, TId>();
        configure(slicer);

        Slicer = slicer;
    }

    /// <summary>
    ///     Aggregate events by the containing stream identity
    /// </summary>
    public void AggregateByStream()
    {
        if (typeof(TId) == typeof(Guid))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamId<TDoc>();
        }
        else if (typeof(TId) == typeof(string))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamKey<TDoc>();
        }
        else if (typeof(TId).GetProperties().Any(x => x.PropertyType == typeof(Guid)))
        {
            Slicer = new ByStreamId<TDoc, TId>(new StoreOptions().RegisterValueType(typeof(TId)));
        }
        else if (typeof(TId).GetProperties().Any(x => x.PropertyType == typeof(string)))
        {
            Slicer = new ByStreamKey<TDoc, TId>(new StoreOptions().RegisterValueType(typeof(TId)));
        }
        else
        {
            throw new InvalidProjectionException(
                $"Invalid identity type {typeof(TId).NameInCode()} for aggregating by stream in projection {GetType().FullNameInCode()}");
        }
    }

    public Type AggregateType
    {
        get => typeof(TDoc);
    }

    public Type[] AllEventTypes { get; set; }
    public bool MatchesAnyDeleteType(StreamAction action)
    {
        return false; // just no way of knowing
    }

    public bool MatchesAnyDeleteType(IEventSlice slice)
    {
        return false; // just no way of knowing
    }

    public bool AppliesTo(IEnumerable<Type> eventTypes)
    {
        return true; // just no way of knowing
    }

    public AsyncOptions Options { get; } = new();
    public object ApplyMetadata(object aggregate, IEvent lastEvent)
    {
        if (aggregate is TDoc t) return ApplyMetadata(t, lastEvent);

        return aggregate;
    }

    /// <summary>
    /// Template method that is called on the last event in a slice of events that
    /// are updating an aggregate. This was added specifically to add metadata like "LastModifiedBy"
    /// from the last event to an aggregate with user-defined logic. Override this for your own specific logic
    /// </summary>
    /// <param name="aggregate"></param>
    /// <param name="lastEvent"></param>
    public virtual TDoc ApplyMetadata(TDoc aggregate, IEvent lastEvent)
    {
        return aggregate;
    }

    public void ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        mapping.UseVersionFromMatchingStream =
            Lifecycle == ProjectionLifecycle.Inline && storeOptions.Events.AppendMode == EventAppendMode.Quick && Slicer is ISingleStreamSlicer;
    }

    TDoc ILiveAggregator<TDoc>.Build(IReadOnlyList<IEvent> events, IQuerySession session, TDoc snapshot)
    {
        throw new NotSupportedException("It's not supported to do a synchronous, live aggregation with a custom projection");
    }

    async ValueTask<TDoc> ILiveAggregator<TDoc>.BuildAsync(IReadOnlyList<IEvent> events, IQuerySession session, TDoc snapshot, CancellationToken cancellation)
    {
        if (!events.Any()) return default;

        var documentSessionBase = session as DocumentSessionBase ?? (DocumentSessionBase)session.DocumentStore.LightweightSession();

        var latestEvent = events.Last();
        var streamId = IdentityFromEvent(documentSessionBase.Options.EventGraph.StreamIdentity, latestEvent);
        var slice = new EventSlice<TDoc, TId>(streamId, session, events);
        if (Lifecycle == ProjectionLifecycle.Live)
        {
            slice.Aggregate = await BuildAsync(session, slice.Aggregate, slice.Events()).ConfigureAwait(false);
            ApplyMetadata(slice.Aggregate, events.Last());
        }
        else
        {
            await ApplyChangesAsync(documentSessionBase, slice, cancellation).ConfigureAwait(false);
        }

        return slice.Aggregate;
    }

    // TODO -- duplicated with AggregationRuntime, and that's an ick.
    /// <summary>
    /// If more than 0 (the default), this is the maximum number of aggregates
    /// that will be cached in a 2nd level, most recently used cache during async
    /// projection. Use this to potentially improve async projection throughput
    /// </summary>
    public int CacheLimitPerTenant { get; set; } = 0;

    private ImHashMap<Tenant, IAggregateCache<TId, TDoc>> _caches = ImHashMap<Tenant, IAggregateCache<TId, TDoc>>.Empty;
    private readonly object _cacheLock = new();

    public IAggregateCache<TId, TDoc> CacheFor(Tenant tenant)
    {
        if (_caches.TryFind(tenant, out var cache)) return cache;

        lock (_cacheLock)
        {
            if (_caches.TryFind(tenant, out cache)) return cache;

            cache = CacheLimitPerTenant == 0
                ? new NulloAggregateCache<TId, TDoc>()
                : new RecentlyUsedCache<TId, TDoc> { Limit = CacheLimitPerTenant };

            _caches = _caches.AddOrUpdate(tenant, cache);

            return cache;
        }
    }

    public TId IdentityFromEvent(StreamIdentity streamIdentity, IEvent e)
        => e.IdentityFromEvent<TId>(streamIdentity);
}
