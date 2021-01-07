using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public class AggregatedPage<TDoc, TId>
    {
        protected readonly DocumentStore _store;
        private readonly IAsyncAggregation<TDoc, TId> _aggregation;

        // TODO -- this needs to be connected to the
        private readonly TransformBlock<EventSlice<TDoc, TId>, IStorageOperation> _builder;
        private Task<Task> _splitting;


        private readonly IQuerySession _session;
        private IncrementalUpdateBatch _batch;
        private readonly IDocumentStorage<TDoc, TId> _storage;

        public AggregatedPage(DocumentStore store, IAsyncAggregation<TDoc, TId> aggregation)
        {
            _store = store;
            _aggregation = aggregation;

            _storage = aggregation.Storage;

            // TODO -- this will change maybe when we have the "pre fetch"
            // options.
            // Watch https://github.com/JasperFx/marten/issues/1627
            _session = _store.LightweightSession();
            _batch = new IncrementalUpdateBatch((DocumentSessionBase) _session);

            // TODO -- pass along a real CancellationToken
            _builder = new TransformBlock<EventSlice<TDoc, TId>, IStorageOperation>(fragment => _aggregation.DetermineOperation((IMartenSession) _session, fragment, CancellationToken.None));

        }


        public void EnqueueDelete(EventSlice<TDoc, TId> fragment)
        {
            var deletion = _aggregation.Storage.DeleteForId(fragment.Id);
            _batch.Enqueue(deletion);
        }


        public long Floor { get; private set; }
        public long Ceiling { get; private set;}

        public async Task<IUpdateBatch> Complete(CancellationToken cancellation)
        {
            await _splitting;

            await _builder.Completion;
            await _batch.Completion;

            return _batch;
        }


        public void StartLoadingEvents(long floor, long ceiling, IAsyncEnumerable<IEvent> events)
        {
            Floor = floor;
            Ceiling = ceiling;

            _splitting = Task.Factory.StartNew(async () =>
            {
                var slices = await _aggregation.Slicer.Slice(events, _store.Tenancy);

                var beingFetched = new List<EventSlice<TDoc, TId>>();

                foreach (var slice in slices)
                {
                    if (_aggregation.WillDelete(slice))
                    {
                        EnqueueDelete(slice);
                    }
                    else if (IsNew(slice))
                    {
                        _builder.Post(slice);
                    }
                    else
                    {
                        beingFetched.Add(slice);
                    }
                }

                var byTenant = beingFetched.GroupBy(x => x.Tenant);
                foreach (var group in byTenant)
                {
                    await startWithExistingAggregates(@group);
                }






            });

        }

        private async Task startWithExistingAggregates(IGrouping<ITenant, EventSlice<TDoc, TId>> @group)
        {
            using var query = _store.QuerySession(@group.Key.TenantId);
            var dict = @group.ToDictionary(x => x.Id);
            var aggregates = await _storage
                .LoadManyAsync(dict.Keys.ToArray(), (IMartenSession) query, CancellationToken.None);

            foreach (var aggregate in aggregates)
            {
                var id = _storage.Identity(aggregate);
                if (dict.TryGetValue(id, out var fragment))
                {
                    fragment.Aggregate = aggregate;
                }
            }

            foreach (var slice in @group)
            {
                _builder.Post(slice);
            }
        }

        // TODO -- Override for the ViewProjection!
        public virtual bool IsNew(EventSlice<TDoc, TId> slice)
        {
            return slice.Events.First().Version == 1;
        }

        public void Dispose()
        {
            _store?.Dispose();
            _splitting?.Dispose();
            _session?.Dispose();
        }
    }
}
