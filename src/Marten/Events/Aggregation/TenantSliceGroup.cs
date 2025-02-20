using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Slicing;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public interface ITenantSliceGroup<TId>: IEventGrouping<TId>, IDisposable
{

}

/// <summary>
///     Intermediate grouping of events by tenant within the asynchronous projection support. Really for aggregations
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public class TenantSliceGroup<TDoc, TId>: ITenantSliceGroup<TId>
{
    private ActionBlock<EventSlice<TDoc, TId>> _builder;

    private ProjectionDocumentSession _session;

    public TenantSliceGroup(IQuerySession session, string tenantId): this(new Tenant(tenantId, session.Database))
    {
    }

    public TenantSliceGroup(Tenant tenant)
    {
        Tenant = tenant;
        Slices = new LightweightCache<TId, EventSlice<TDoc, TId>>(id => new EventSlice<TDoc, TId>(id, Tenant.TenantId));
    }

    public TenantSliceGroup(Tenant tenant, IEnumerable<EventSlice<TDoc, TId>> slices): this(tenant)
    {
        foreach (var slice in slices) Slices[slice.Id] = slice;
    }

    public Tenant Tenant { get; }
    public LightweightCache<TId, EventSlice<TDoc, TId>> Slices { get; }

    public void AddEvents<TEvent>(Func<TEvent, TId> singleIdSource, IEnumerable<IEvent> events)
    {
        AddEventsWithMetadata<TEvent>(e => singleIdSource(e.Data), events);
    }

    /// <summary>
    ///     Add events to the grouping based on the outer IEvent<TEvent> envelope type
    /// </summary>
    /// <param name="singleIdSource"></param>
    /// <param name="events"></param>
    /// <typeparam name="TEvent"></typeparam>
    public void AddEventsWithMetadata<TEvent>(Func<IEvent<TEvent>, TId> singleIdSource, IEnumerable<IEvent> events)
    {
        var matching = events.OfType<IEvent<TEvent>>();
        foreach (var @event in matching)
        {
            var id = singleIdSource(@event);
            AddEvent(id, @event);
        }
    }

    public void FanOutOnEach<TSource, TChild>(Func<TSource, IEnumerable<TChild>> fanOutFunc)
    {
        foreach (var slice in Slices) slice.FanOut(fanOutFunc);
    }

    public void AddEvents<TEvent>(Func<TEvent, IEnumerable<TId>> multipleIdSource, IEnumerable<IEvent> events)
    {
        AddEventsWithMetadata<TEvent>(e => multipleIdSource(e.Data), events);
    }

    /// <summary>
    ///     Add events to the grouping based on the outer IEvent<TEvent> envelope type
    /// </summary>
    /// <param name="singleIdSource"></param>
    /// <param name="events"></param>
    /// <typeparam name="TEvent"></typeparam>
    public void AddEventsWithMetadata<TEvent>(Func<IEvent<TEvent>, IEnumerable<TId>> multipleIdSource, IEnumerable<IEvent> events)
    {
        var matching = events.OfType<IEvent<TEvent>>()
            .SelectMany(e => multipleIdSource(e).Select(id => (id, @event: e)));

        var groups = matching.GroupBy(x => x.id);
        foreach (var group in groups) AddEvents(group.Key, group.Select(x => x.@event));
    }

    public void AddEvent(TId id, IEvent @event)
    {
        if (id != null)
        {
            Slices[id].AddEvent(@event);
        }

    }

    public void AddEvents(TId id, IEnumerable<IEvent> events)
    {
        if (id != null)
        {
            Slices[id].AddEvents(events);
        }
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    internal async Task Start(ProjectionUpdateBatch updateBatch,
        IAggregationRuntime<TDoc, TId> runtime,
        DocumentStore store,
        EventRangeGroup parent)
    {
        _session = new ProjectionDocumentSession(store, updateBatch,
            new SessionOptions { Tracking = DocumentTracking.None, Tenant = Tenant }, updateBatch.Mode);

        _builder = new ActionBlock<EventSlice<TDoc, TId>>(async slice =>
        {
            if (parent.Cancellation.IsCancellationRequested)
            {
                return;
            }

            // TODO -- emit exceptions in one place
            await runtime.ApplyChangesAsync(_session, slice, parent.Cancellation, ProjectionLifecycle.Async)
                .ConfigureAwait(false);
        }, new ExecutionDataflowBlockOptions { CancellationToken = parent.Cancellation });

        await processEventSlices(runtime, store, parent.Cancellation).ConfigureAwait(false);

        var builder = Volatile.Read(ref _builder);

        if (builder != null)
        {
            builder.Complete();
            await builder.Completion.ConfigureAwait(false);
        }
    }

    private async Task processEventSlices(IAggregationRuntime<TDoc, TId> runtime,
        IDocumentStore store, CancellationToken token)
    {
        var cache = runtime.CacheFor(Tenant);
        var beingFetched = new List<EventSlice<TDoc, TId>>();
        foreach (var slice in Slices)
        {
            if (token.IsCancellationRequested)
            {
                _builder.Complete();
                break;
            }

            if (runtime.IsNew(slice))
            {
                _builder.Post(slice);

                // Don't use it any farther, it's ready to do its thing
                Slices.Remove(slice.Id);
            }
            else if (cache.TryFind(slice.Id, out var aggregate))
            {
                slice.Aggregate = aggregate;
                _builder.Post(slice);

                // Don't use it any farther, it's ready to do its thing
                Slices.Remove(slice.Id);
            }
            else
            {
                beingFetched.Add(slice);
            }
        }

        if (token.IsCancellationRequested || !beingFetched.Any())
        {
            cache.CompactIfNecessary();
            return;
        }

        // Minor optimization
        if (!beingFetched.Any())
        {
            return;
        }

        var ids = beingFetched.Select(x => x.Id).ToArray();

        var options = new SessionOptions { Tenant = Tenant, AllowAnyTenant = true };

        await using var session = (IMartenSession)store.LightweightSession(options);
        var aggregates = await runtime.Storage
            .LoadManyAsync(ids, session, token).ConfigureAwait(false);

        if (token.IsCancellationRequested || aggregates == null)
        {
            return;
        }

        var dict = aggregates.ToDictionary(x => runtime.Storage.Identity(x));

        foreach (var slice in Slices)
        {
            if (dict.TryGetValue(slice.Id, out var aggregate))
            {
                slice.Aggregate = aggregate;
                cache.Store(slice.Id, aggregate);
            }

            _builder?.Post(slice);
        }
    }

    internal void ApplyFanOutRules(IReadOnlyList<IFanOutRule> rules)
    {
        foreach (var slice in Slices) slice.ApplyFanOutRules(rules);
    }

    public void Reset()
    {
        _builder?.Complete();
        _builder = null;
    }
}
