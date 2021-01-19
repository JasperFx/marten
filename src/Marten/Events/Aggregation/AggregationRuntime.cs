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
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public abstract class AggregationRuntime<TDoc, TId>: IAsyncCapableProjection
    {
        private readonly IDocumentStore _store;
        public IDocumentStorage<TDoc, TId> Storage { get; }
        public IAggregateProjection Projection { get;}
        public IEventSlicer<TDoc, TId> Slicer { get;}

        public ITenancy Tenancy { get;}

        public AggregationRuntime(IDocumentStore store, IAggregateProjection projection, IEventSlicer<TDoc, TId> slicer, ITenancy tenancy, IDocumentStorage<TDoc, TId> storage)
        {
            Projection = projection;
            Slicer = slicer;
            Storage = storage;
            Tenancy = tenancy;
            _store = store;
        }

        public abstract Task<IStorageOperation> DetermineOperation(DocumentSessionBase session,
            EventSlice<TDoc, TId> slice, CancellationToken cancellation);

        public Task Configure(ActionBlock<IStorageOperation> queue, IReadOnlyList<TenantSliceGroup<TDoc, TId>> groups,
            CancellationToken token)
        {
            foreach (var @group in groups)
            {
                @group.Start(queue, this, _store, token);
            }

            return Task.WhenAll(groups.Select(x => x.Complete()).ToArray());
        }


        public virtual bool IsNew(EventSlice<TDoc, TId> slice)
        {
            return slice.Events.First().Version == 1;
        }

        public void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams)
        {
            ApplyAsync(session, streams, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            var slices = Slicer.Slice(streams, Tenancy);

            var martenSession = (DocumentSessionBase)session;
            foreach (var slice in slices)
            {

                IStorageOperation operation = null;
                if (Projection.MatchesAnyDeleteType(slice))
                {
                    operation = Storage.DeleteForId(slice.Id, slice.Tenant);
                }
                else
                {
                    operation = await DetermineOperation(martenSession, slice, cancellation);
                }

                session.QueueOperation(operation);
            }
        }

        public IReadOnlyList<IAsyncProjectionShard> AsyncProjectionShards(IDocumentStore store, ITenancy tenancy)
        {
            // TODO -- support sharding
            // TODO -- use event filters!!!
            var shard = new AggregationShard<TDoc, TId>(Projection.ProjectionName, new ISqlFragment[0], this, tenancy);

            return new List<IAsyncProjectionShard> {shard};
        }
    }
}
