using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ImTools;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public abstract class AggregationRuntime<TDoc, TId> : IProjection
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

        public async Task<IStorageOperation> DetermineOperation(DocumentSessionBase session,
            EventSlice<TDoc, TId> slice, CancellationToken cancellation, ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
        {
            var aggregate = slice.Aggregate;

            if (slice.Aggregate == null && lifecycle == ProjectionLifecycle.Inline)
            {
                aggregate = await Storage.LoadAsync(slice.Id, session, cancellation);
            }

            var exists = aggregate != null;

            foreach (var @event in slice.Events)
            {
                aggregate = await ApplyEvent(session, slice, @event, aggregate, cancellation);
            }

            if (aggregate != null)
            {
                Storage.SetIdentity(aggregate, slice.Id);
            }

            if (aggregate == null)
            {
                return exists ? Storage.DeleteForId(slice.Id, slice.Tenant) : null;
            }

            return Storage.Upsert(aggregate, session, slice.Tenant);
        }

        public abstract ValueTask<TDoc> ApplyEvent(IQuerySession session, EventSlice<TDoc, TId> slice,
            IEvent evt, TDoc aggregate,
            CancellationToken cancellationToken);

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

        public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            ApplyAsync(operations, streams, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation)
        {
            // Doing the filtering here to prevent unnecessary network round trips by allowing
            // an aggregate projection to "work" on a stream with no matching events
            var filteredStreams = streams
                .Where(x => Projection.AppliesTo(x.Events.Select(x => x.EventType)))
                .ToArray();

            var slices = Slicer.Slice(filteredStreams, Tenancy);

            var martenSession = (DocumentSessionBase)operations;
            foreach (var slice in slices)
            {
                IStorageOperation operation = null;

                // TODO -- this can only apply to the last event
                if (Projection.MatchesAnyDeleteType(slice))
                {
                    operation = Storage.DeleteForId(slice.Id, slice.Tenant);
                }
                else
                {
                    operation = await DetermineOperation(martenSession, slice, cancellation);
                }

                if (operation != null) operations.QueueOperation(operation);
            }
        }


    }
}
