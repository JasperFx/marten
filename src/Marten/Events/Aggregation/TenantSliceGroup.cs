using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Aggregation
{


    public interface ITenantSliceGroup<TId>
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
    public class TenantSliceGroup<TDoc, TId> : IDisposable, ITenantSliceGroup<TId>
    {
        public ITenant Tenant { get; }
        public LightweightCache<TId, EventSlice<TDoc, TId>> Slices { get; }
        private TransformBlock<EventSlice<TDoc, TId>, IStorageOperation> _builder;
        private Task<Task> _application;

        public TenantSliceGroup(ITenant tenant)
        {
            Tenant = tenant;
            Slices = new LightweightCache<TId, EventSlice<TDoc, TId>>(id => new EventSlice<TDoc, TId>(id, Tenant));
        }

        public TenantSliceGroup(ITenant tenant, IEnumerable<EventSlice<TDoc, TId>> slices) : this(tenant)
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

        internal void Start(IShardAgent shardAgent, ActionBlock<IStorageOperation> queue,
            AggregationRuntime<TDoc, TId> runtime,
            IDocumentStore store, EventRangeGroup parent)
        {
            _builder = new TransformBlock<EventSlice<TDoc, TId>, IStorageOperation>(async slice =>
            {
                if (parent.Cancellation.IsCancellationRequested) return null;

                IStorageOperation operation = null;

                await shardAgent.TryAction(async () =>
                {
                    using var session = (DocumentSessionBase) store.LightweightSession(slice.Tenant.TenantId);

                    operation = await runtime.DetermineOperation(session, slice, parent.Cancellation, ProjectionLifecycle.Async);
                }, parent.Cancellation, group:parent, logException: (l, e) =>
                {
                    l.LogError(e, "Failure trying to build a storage operation to update {DocumentType} with {Id}", typeof(TDoc).FullNameInCode(), slice.Id);
                }, actionMode:GroupActionMode.Child);

                return operation;
            }, new ExecutionDataflowBlockOptions
            {
                CancellationToken = parent.Cancellation,
            });

            _builder.LinkTo(queue, x => x != null);

            _application = Task.Factory.StartNew(() =>
                processEventSlices(shardAgent, runtime, store, parent.Cancellation)
                , parent.Cancellation);

        }

        private async Task processEventSlices(IShardAgent shardAgent, AggregationRuntime<TDoc, TId> runtime,
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
                        .LoadManyAsync(ids, session, token);
                }
            }, token);

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

        internal async Task Complete()
        {
            if (_application != null)
            {
                await (await _application);
            }

            // This can happen if one group fails early
            if (_builder != null)
            {
                _builder.Complete();
                await _builder.Completion;
            }
        }

        public void Dispose()
        {
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
