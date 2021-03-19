using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public class TenantSliceGroup<TDoc, TId> : IDisposable
    {
        public ITenant Tenant { get; }
        public readonly IReadOnlyList<EventSlice<TDoc, TId>> Slices;
        private TransformBlock<EventSlice<TDoc, TId>, IStorageOperation> _builder;
        private Task<Task> _application;

        public TenantSliceGroup(ITenant tenant, IEnumerable<EventSlice<TDoc, TId>> slices)
        {
            Tenant = tenant;
            Slices = new List<EventSlice<TDoc, TId>>(slices);
        }

        internal void Start(IShardAgent shardAgent, ActionBlock<IStorageOperation> queue,
            AggregationRuntime<TDoc, TId> runtime,
            IDocumentStore store, CancellationToken token)
        {
            _builder = new TransformBlock<EventSlice<TDoc, TId>, IStorageOperation>(async slice =>
            {
                if (token.IsCancellationRequested) return null;

                IStorageOperation operation = null;

                await shardAgent.TryAction(async () =>
                {
                    using var session = (DocumentSessionBase) store.LightweightSession(slice.Tenant.TenantId);

                    operation = await runtime.DetermineOperation(session, slice, token, ProjectionLifecycle.Async);
                }, token);

                return operation;
            }, new ExecutionDataflowBlockOptions
            {
                CancellationToken = token,
            });

            _builder.LinkTo(queue, x => x != null);

            _application = Task.Factory.StartNew(() =>
                processEventSlices(shardAgent, runtime, store, token)
                , token);

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
            await _application;
            _builder.Complete();
            await _builder.Completion;
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
