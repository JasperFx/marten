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

        internal void Start(ActionBlock<IStorageOperation> queue, AggregationRuntime<TDoc, TId> runtime,
            IDocumentStore store, CancellationToken token)
        {
            _session = (DocumentSessionBase)store.LightweightSession(Tenant.TenantId);

            _builder = new TransformBlock<EventSlice<TDoc, TId>, IStorageOperation>(slice =>
            {
                if (token.IsCancellationRequested) return null;

                try
                {
                    return runtime.DetermineOperation(_session, slice, token);
                }
                catch (Exception e)
                {
                    // TODO -- throw a specific error so you can capture the event information
                    // to detect poison pill messages
                    Debug.WriteLine(e);
                    throw;
                }
            }, new ExecutionDataflowBlockOptions
            {
                CancellationToken = token
            });

            _builder.LinkTo(queue, x => x != null);

            _application = Task.Factory.StartNew(async () =>
            {
                var beingFetched = new List<EventSlice<TDoc, TId>>();
                foreach (var slice in Slices)
                {
                    if (token.IsCancellationRequested)
                    {
                        _builder.Complete();
                        break;
                    }

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

                if (token.IsCancellationRequested) return;

                var ids = beingFetched.Select(x => x.Id).ToArray();
                var aggregates = await runtime.Storage
                    .LoadManyAsync(ids, _session, token);

                if (token.IsCancellationRequested) return;

                var dict = aggregates.ToDictionary(x => runtime.Storage.Identity(x));

                foreach (var slice in Slices)
                {
                    if (dict.TryGetValue(slice.Id, out var aggregate))
                    {
                        slice.Aggregate = aggregate;
                    }

                    _builder.Post(slice);
                }
            }, token);
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

        internal void ApplyFanOutRules(IReadOnlyList<IFanOutRule> rules)
        {
            foreach (var slice in Slices)
            {
                slice.ApplyFanOutRules(rules);
            }
        }

        public void Reset()
        {
            _session?.Dispose();
            _builder?.Complete();
            _builder = null;
        }
    }
}
