#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;
using Npgsql;

namespace Marten.Events.Aggregation;

public abstract class CrossStreamAggregationRuntime<TDoc, TId>: AggregationRuntime<TDoc, TId>
    where TDoc : notnull where TId : notnull
{
    public CrossStreamAggregationRuntime(IDocumentStore store, IAggregateProjection projection,
        IEventSlicer<TDoc, TId> slicer, IDocumentStorage<TDoc, TId> storage): base(store, projection, slicer, storage)
    {
    }

    public override bool IsNew(EventSlice<TDoc, TId> slice)
    {
        return false;
    }
}

/// <summary>
///     Internal base class for runtime event aggregation
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class AggregationRuntime<TDoc, TId>: IAggregationRuntime<TDoc, TId>
    where TDoc : notnull where TId : notnull
{
    public AggregationRuntime(IDocumentStore store, IAggregateProjection projection, IEventSlicer<TDoc, TId> slicer,
        IDocumentStorage<TDoc, TId> storage)
    {
        Projection = projection;
        Slicer = slicer;
        Storage = storage;
    }

    public IAggregateProjection Projection { get; }
    public IEventSlicer<TDoc, TId> Slicer { get; }
    public IDocumentStorage<TDoc, TId> Storage { get; }

    public async ValueTask ApplyChangesAsync(DocumentSessionBase session,
        EventSlice<TDoc, TId> slice, CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {

        session = UseTenancyBasedOnSliceAndStorage(session, slice);

        if (Projection.MatchesAnyDeleteType(slice))
        {
            var operation = Storage.DeleteForId(slice.Id, slice.Tenant.TenantId);
            session.QueueOperation(operation);
            return;
        }

        var aggregate = slice.Aggregate;
        if (slice.Aggregate == null && lifecycle == ProjectionLifecycle.Inline)
        {
            aggregate = await Storage.LoadAsync(slice.Id, session, cancellation).ConfigureAwait(false);
        }

        // Does the aggregate already exist before the events are applied?
        var exists = aggregate != null;

        foreach (var @event in slice.Events())
        {
            try
            {
                aggregate = await ApplyEvent(session, slice, @event, aggregate, cancellation).ConfigureAwait(false);
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
            Versioning.TrySetVersion(aggregate, slice.Events().LastOrDefault());
        }

        // Delete the aggregate *if* it existed prior to these events
        if (aggregate == null)
        {
            if (exists)
            {
                var operation = Storage.DeleteForId(slice.Id, slice.Tenant.TenantId);
                session.QueueOperation(operation);
            }

            return;
        }

        session.QueueOperation(Storage.Upsert(aggregate, session, slice.Tenant.TenantId));
    }

    private DocumentSessionBase UseTenancyBasedOnSliceAndStorage(DocumentSessionBase session, EventSlice<TDoc, TId> slice)
    {
        var shouldApplyConjoinedTenancy = Storage.TenancyStyle == TenancyStyle.Conjoined
                                          && slice.Tenant.TenantId != Tenancy.DefaultTenantId
                                          && session.TenantId != slice.Tenant.TenantId;

        var shouldApplyDefaultTenancy = Storage.TenancyStyle == TenancyStyle.Single
                                        && session.TenantId != Tenancy.DefaultTenantId;

        return shouldApplyConjoinedTenancy || shouldApplyDefaultTenancy
            ? (DocumentSessionBase)session.ForTenant(
                !shouldApplyDefaultTenancy
                    ? slice.Tenant.TenantId
                    : Tenancy.DefaultTenantId
            )
            : session;
    }

    public IAggregateVersioning Versioning { get; set; }


    public virtual bool IsNew(EventSlice<TDoc, TId> slice)
    {
        return slice.Events().First().Version == 1;
    }

    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
#pragma warning disable VSTHRD002
        ApplyAsync(operations, streams, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        // Doing the filtering here to prevent unnecessary network round trips by allowing
        // an aggregate projection to "work" on a stream with no matching events
        var filteredStreams = streams
            .Where(x => Projection.AppliesTo(x.Events.Select(x => x.EventType)))
            .ToArray();

        var slices = await Slicer.SliceInlineActions(operations, filteredStreams).ConfigureAwait(false);

        var martenSession = (DocumentSessionBase)operations;

        await martenSession.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        foreach (var slice in slices)
        {
            await ApplyChangesAsync(martenSession, slice, cancellation).ConfigureAwait(false);
        }
    }

    public async ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase database, EventRange range,
        CancellationToken cancellationToken)
    {
        var groups = await GroupEventRange(store, database, range, cancellationToken).ConfigureAwait(false);

        return new TenantSliceRange<TDoc, TId>(store, this, range, groups, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> GroupEventRange(DocumentStore store,
        IMartenDatabase database, EventRange range, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession(SessionOptions.ForDatabase(database));
        return await Slicer.SliceAsyncEvents(session, range.Events).ConfigureAwait(false);
    }

    public abstract ValueTask<TDoc> ApplyEvent(IQuerySession session, EventSlice<TDoc, TId> slice,
        IEvent evt, TDoc? aggregate,
        CancellationToken cancellationToken);
}
