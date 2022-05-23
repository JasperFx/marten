using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    /// <summary>
    /// Internal interface for runtime event aggregation
    /// </summary>
    public interface IAggregationRuntime : IProjection
    {
        ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase database, EventRange range,
            CancellationToken cancellationToken);

        IAggregateVersioning Versioning { get; set; }
    }

    public interface IAggregationRuntime<TDoc, TId>: IAggregationRuntime where TDoc : notnull where TId : notnull
    {
        ValueTask ApplyChangesAsync(DocumentSessionBase session,
            EventSlice<TDoc, TId> slice, CancellationToken cancellation,
            ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline);

        bool IsNew(EventSlice<TDoc, TId> slice);
        IDocumentStorage<TDoc, TId> Storage { get; }

        [Obsolete]
        ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> GroupEventRange(DocumentStore store,
            IMartenDatabase database, EventRange range, CancellationToken cancellation);
    }
}

