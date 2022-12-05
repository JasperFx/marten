using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Events.Aggregation;

/// <summary>
///     Internal interface for runtime event aggregation
/// </summary>
public interface IAggregationRuntime: IProjection
{
    IAggregateVersioning Versioning { get; set; }

    ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase database, EventRange range,
        CancellationToken cancellationToken);
}

public interface IAggregationRuntime<TDoc, TId>: IAggregationRuntime where TDoc : notnull where TId : notnull
{
    IDocumentStorage<TDoc, TId> Storage { get; }

    ValueTask ApplyChangesAsync(DocumentSessionBase session,
        EventSlice<TDoc, TId> slice, CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline);

    bool IsNew(EventSlice<TDoc, TId> slice);

    [Obsolete]
    ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> GroupEventRange(DocumentStore store,
        IMartenDatabase database, EventRange range, CancellationToken cancellation);
}
