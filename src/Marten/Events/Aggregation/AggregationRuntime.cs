using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;
using Npgsql;
#nullable enable
namespace Marten.Events.Aggregation
{
    /// <summary>
    /// Internal interface for runtime event aggregation
    /// </summary>
    public interface IAggregationRuntime : IProjection
    {
        EventRangeGroup GroupEvents(DocumentStore store, EventRange range, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Internal base class for runtime event aggregation
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TId"></typeparam>
    public abstract class AggregationRuntime<TDoc, TId> : IAggregationRuntime where TDoc : notnull where TId : notnull
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

        public async Task<IStorageOperation?> DetermineOperation(DocumentSessionBase session,
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
                try
                {
                    aggregate = await ApplyEvent(session, slice, @event, aggregate, cancellation);
                }
                catch (MartenCommandException)
                {
                    throw;
                }
                catch (NpgsqlException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new ApplyEventException(@event, e);
                }
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
            IEvent evt, TDoc? aggregate,
            CancellationToken cancellationToken);


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

            var slices = Slicer.Slice(operations, filteredStreams, Tenancy);

            var martenSession = (DocumentSessionBase)operations;
            foreach (var slice in slices)
            {
                IStorageOperation? operation;

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

        public EventRangeGroup GroupEvents(DocumentStore store, EventRange range, CancellationToken cancellationToken)
        {
            IReadOnlyList<TenantSliceGroup<TDoc, TId>> groups;

            using (var session = store.QuerySession())
            {
                groups = Slicer.Slice(session, range.Events, store.Tenancy);
            }

            return new TenantSliceRange<TDoc, TId>(store, this, range, groups, cancellationToken);
        }
    }
}
