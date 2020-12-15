using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;

namespace Marten.Events.V4Concept.Aggregation
{
    public class AggregatedPage<TDoc, TId> : IAsyncBatch
    {
        protected readonly DocumentStore _store;
        private readonly IAsyncAggregation<TDoc, TId> _aggregation;
        protected readonly IDocumentStorage<TDoc, TId> _storage;

        private readonly ActionBlock<IStorageOperation> _enqueueOperations;

        // TODO -- this needs to be connected to the
        private readonly TransformBlock<StreamFragment<TDoc, TId>, IStorageOperation> _builder;
        private Task<Task> _splitting;

        // TODO -- replace when https://github.com/JasperFx/marten/issues/1627 is done.
        private readonly List<IStorageOperation> _operations = new List<IStorageOperation>();
        private readonly IQuerySession _session;

        public AggregatedPage(DocumentStore store, IAsyncAggregation<TDoc, TId> aggregation)
        {
            _store = store;
            _aggregation = aggregation;

            // TODO -- this will change maybe when we have the "pre fetch"
            // options.
            // Watch https://github.com/JasperFx/marten/issues/1627
            _session = _store.QuerySession();

            _enqueueOperations = new ActionBlock<IStorageOperation>(x => _operations.Add(x),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1
                });

            // TODO -- pass along a real CancellationToken
            _builder = new TransformBlock<StreamFragment<TDoc, TId>, IStorageOperation>(fragment => _aggregation.DetermineOperation((IMartenSession) _session, fragment, CancellationToken.None));

        }


        public void EnqueueDelete(StreamFragment<TDoc, TId> fragment)
        {
            var deletion = _storage.DeleteForId(fragment.Id);
            _enqueueOperations.Post(deletion);
        }


        public long Floor { get; private set; }
        public long Ceiling { get; private set;}

        public async Task<IUpdateBatch> Complete(CancellationToken cancellation)
        {
            await _splitting;

            await _builder.Completion;
            await _enqueueOperations.Completion;

            return new UpdateBatch(_operations);
        }


        public int Count { get; private set;}

        public void StartLoadingEvents(long floor, long ceiling, IReadOnlyList<IEvent> events)
        {
            Count = events.Count;
            Floor = floor;
            Ceiling = ceiling;


            _splitting = Task.Factory.StartNew(async () =>
            {
                var fragments = _aggregation.Split(events, _store.Tenancy);

                var beingFetched = new Dictionary<TId, StreamFragment<TDoc, TId>>();

                foreach (var fragment in fragments)
                {
                    if (_aggregation.WillDelete(fragment))
                    {
                        EnqueueDelete(fragment);
                    }
                    else if (IsNew(fragment))
                    {
                        _builder.Post(fragment);
                    }
                    else
                    {
                        beingFetched.Add(fragment.Id, fragment);
                    }
                }

                using (var query = _store.QuerySession())
                {
                    // TODO -- THIS DOES NOT WORK IF MULTI-TENANTED. NEED TO BREAK UP BY TENANT
                    // TODO -- pass along the right CancellationToken
                    var aggregates = await _storage
                        .LoadManyAsync(beingFetched.Keys.ToArray(), (IMartenSession)query, CancellationToken.None);

                    foreach (var aggregate in aggregates)
                    {
                        var id = _storage.Identity(aggregate);
                        if (beingFetched.TryGetValue(id, out var fragment))
                        {
                            fragment.Aggregate = aggregate;
                        }
                    }
                }

                foreach (var fragment in beingFetched.Values)
                {
                    _builder.Post(fragment);
                }
            });

        }


        public virtual bool IsNew(StreamFragment<TDoc, TId> fragment)
        {
            return fragment.Events.First().Version == 1;
        }

        public void Dispose()
        {
            _store?.Dispose();
            _splitting?.Dispose();
            _session?.Dispose();
        }
    }
}
