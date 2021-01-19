using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public class TenantSliceGroup<TDoc, TId> : IDisposable
    {
        public ITenant Tenant { get; }
        public readonly IReadOnlyList<EventSlice<TDoc, TId>> Slices;
        private DocumentSessionBase _session;
        private TransformBlock<EventSlice<TDoc, TId>, IStorageOperation> _builder;
        private Task<Task> _application;

        public TenantSliceGroup(ITenant tenant, IEnumerable<EventSlice<TDoc, TId>> slices)
        {
            Tenant = tenant;
            Slices = new List<EventSlice<TDoc, TId>>(slices);
        }

        internal void Start(ActionBlock<IStorageOperation> queue, AggregationRuntime<TDoc, TId> runtime, IDocumentStore store)
        {
            _session = (DocumentSessionBase)store.LightweightSession(Tenant.TenantId);

            _builder = new TransformBlock<EventSlice<TDoc, TId>, IStorageOperation>(slice =>
            {
                try
                {
                    return runtime.DetermineOperation(_session, slice, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    throw;
                }
            });

            _builder.LinkTo(queue);

            _application = Task.Factory.StartNew(async () =>
            {
                var beingFetched = new List<EventSlice<TDoc, TId>>();
                foreach (var slice in Slices)
                {
                    if (runtime.Projection.MatchesAnyDeleteType(slice))
                    {
                        var deletion = runtime.Storage.DeleteForId(slice.Id, Tenant);
                        queue.Post(deletion);
                    }
                    else if (runtime.IsNew(slice))
                    {
                        _builder.Post(slice);
                    }
                    else
                    {
                        beingFetched.Add(slice);
                    }
                }

                var ids = beingFetched.Select(x => x.Id).ToArray();
                var aggregates = await runtime.Storage
                    .LoadManyAsync(ids, _session, CancellationToken.None); // TODO -- pass a real cancellation around

                var dict = aggregates.ToDictionary(x => runtime.Storage.Identity(x));

                foreach (var slice in Slices)
                {
                    if (dict.TryGetValue(slice.Id, out var aggregate))
                    {
                        slice.Aggregate = aggregate;
                    }

                    _builder.Post(slice);
                }
            });
        }

        internal async Task Complete()
        {
            await _application;
            _builder.Complete();
            await _builder.Completion;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
