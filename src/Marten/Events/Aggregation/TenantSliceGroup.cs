using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Aggregation
{


    public interface ITenantSliceGroup<TId> : IDisposable
    {
        /// <summary>
        /// Add a single event to a single event slice by id
        /// </summary>
        /// <param name="id">The aggregate id</param>
        /// <param name="event"></param>
        void AddEvent(TId id, IEvent @event);

        /// <summary>
        /// Add many events to a single event slice by aggregate id
        /// </summary>
        /// <param name="id">The aggregate id</param>
        /// <param name="events"></param>
        void AddEvents(TId id, IEnumerable<IEvent> events);

        /// <summary>
        /// Add events to streams where each event of type TEvent applies to only
        /// one stream
        /// </summary>
        /// <param name="singleIdSource"></param>
        /// <param name="events"></param>
        /// <typeparam name="TEvent"></typeparam>
        void AddEvents<TEvent>(Func<TEvent, TId> singleIdSource, IEnumerable<IEvent> events);

        /// <summary>
        /// Add events to streams where each event of type TEvent may be related to many
        /// different aggregates
        /// </summary>
        /// <param name="multipleIdSource"></param>
        /// <param name="events"></param>
        /// <typeparam name="TEvent"></typeparam>
        void AddEvents<TEvent>(Func<TEvent, IEnumerable<TId>> multipleIdSource, IEnumerable<IEvent> events);
    }

    /// <summary>
    /// Intermediate grouping of events by tenant within the asynchronous projection support
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TId"></typeparam>
    public class TenantSliceGroup<TDoc, TId> : ITenantSliceGroup<TId>
    {
        public Tenant Tenant { get; }
        public LightweightCache<TId, EventSlice<TDoc, TId>> Slices { get; }
        private ActionBlock<EventSlice<TDoc, TId>> _builder;

        private ProjectionDocumentSession _session;

        public TenantSliceGroup(IQuerySession session, string tenantId) : this(new Tenant(tenantId, session.Database))
        {
        }

        public TenantSliceGroup(Tenant tenant)
        {
            Tenant = tenant;
            Slices = new LightweightCache<TId, EventSlice<TDoc, TId>>(id => new EventSlice<TDoc, TId>(id, Tenant));
        }

        public TenantSliceGroup(Tenant tenant, IEnumerable<EventSlice<TDoc, TId>> slices) : this(tenant)
        {
            foreach (var slice in slices)
            {
                Slices[slice.Id] = slice;
            }
        }

        public void AddEvents<TEvent>(Func<TEvent, TId> singleIdSource, IEnumerable<IEvent> events)
        {
            var matching = events.Where(x => x.Data is TEvent);
            foreach (var @event in matching)
            {
                var id = singleIdSource((TEvent) @event.Data);
                AddEvent(id, @event);
            }
        }

        public void AddEvents<TEvent>(Func<TEvent, IEnumerable<TId>> multipleIdSource, IEnumerable<IEvent> events)
        {
            var matching = events.Where(x => x.Data is TEvent)
                .SelectMany(@event => multipleIdSource(@event.Data.As<TEvent>()).Select(id => (id, @event)));

            var groups = matching.GroupBy(x => x.id);
            foreach (var @group in groups)
            {
                AddEvents(@group.Key, @group.Select(x => x.@event));
            }
        }

        public void AddEvent(TId id, IEvent @event)
        {
            Slices[id].AddEvent(@event);
        }

        public void AddEvents(TId id, IEnumerable<IEvent> events)
        {
            Slices[id].AddEvents(events);
        }

        internal async Task Start(IShardAgent shardAgent, ProjectionUpdateBatch updateBatch,
            IAggregationRuntime<TDoc, TId> runtime,
            DocumentStore store, EventRangeGroup parent)
        {
            _session = new ProjectionDocumentSession(store, updateBatch, new SessionOptions {Tracking = DocumentTracking.None, TenantId = Tenant.TenantId});

            _builder = new ActionBlock<EventSlice<TDoc, TId>>(async slice =>
            {
                if (parent.Cancellation.IsCancellationRequested) return;

                await shardAgent.TryAction(async () =>
                {
                    await runtime.ApplyChangesAsync(_session, slice, parent.Cancellation, ProjectionLifecycle.Async).ConfigureAwait(false);
                }, parent.Cancellation, @group:parent, logException: (l, e) =>
                {
                    l.LogError(e, "Failure trying to build a storage operation to update {DocumentType} with {Id}", typeof(TDoc).FullNameInCode(), slice.Id);
                }, actionMode:GroupActionMode.Child).ConfigureAwait(false);
            }, new ExecutionDataflowBlockOptions
            {
                CancellationToken = parent.Cancellation,
            });

            await processEventSlices(shardAgent, runtime, store, parent.Cancellation).ConfigureAwait(false);

            var builder = Volatile.Read(ref _builder);

            if (builder != null)
            {
                builder.Complete();
                await builder.Completion.ConfigureAwait(false);
            }
        }

        private async Task processEventSlices(IShardAgent shardAgent, IAggregationRuntime<TDoc, TId> runtime,
            IDocumentStore store, CancellationToken token)
        {
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
                }
                else
                {
                    beingFetched.Add(slice);
                }
            }

            if (token.IsCancellationRequested) return;

            var ids = beingFetched.Select(x => x.Id).ToArray();

            IReadOnlyList<TDoc> aggregates = null;

            await shardAgent.TryAction(async () =>
            {
                using (var session = (IMartenSession) store.LightweightSession(Tenant.TenantId))
                {
                    aggregates = await runtime.Storage
                        .LoadManyAsync(ids, session, token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            if (token.IsCancellationRequested || aggregates == null) return;

            var dict = aggregates.ToDictionary(x => runtime.Storage.Identity(x));

            foreach (var slice in Slices)
            {
                if (dict.TryGetValue(slice.Id, out var aggregate))
                {
                    slice.Aggregate = aggregate;
                }

                _builder.Post(slice);
            }
        }

        public void Dispose()
        {
            _session.Dispose();
        }

        internal void ApplyFanOutRules(IReadOnlyList<IFanOutRule> rules)
        {
            foreach (var slice in Slices)
            {
                slice.ApplyFanOutRules(rules);
            }
        }

        public void Reset()
        {
            _builder?.Complete();
            _builder = null;
        }
    }
}
