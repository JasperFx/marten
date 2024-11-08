using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events.Aggregation;

[Obsolete("Trying to generalize this and get it to JasperFx")]
internal class EventSliceGroup<TDoc, TId>: EventRangeGroup
{
    private readonly IAggregationRuntime<TDoc, TId> _runtime;
    private readonly DocumentStore _store;

    public EventSliceGroup(DocumentStore store, IAggregationRuntime<TDoc, TId> runtime, EventRange range,
        IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>> groups, CancellationToken projectionCancellation): base(range,
        projectionCancellation)
    {
        _store = store;
        _runtime = runtime;
        Groups = groups;
    }

    public IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>> Groups { get; private set; }


    protected override void reset()
    {
    }

    public override void Dispose()
    {
    }

    public override string ToString()
    {
        return $"Aggregate for {Range}, {Groups.Count} slices";
    }

    public override async Task ConfigureUpdateBatch(ProjectionUpdateBatch batch)
    {
        await Parallel.ForEachAsync(Groups, CancellationToken.None,
                async (group, _) =>
                    await batch.ProcessAggregationAsync(group, Cancellation).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    public override async ValueTask SkipEventSequence(long eventSequence, IMartenDatabase database)
    {
        reset();
        Range.SkipEventSequence(eventSequence);
        await using var session = _store.LightweightSession(SessionOptions.ForDatabase(database));
        Groups = await _runtime.Slicer.SliceAsyncEvents(session, Range.Events).ConfigureAwait(false);
    }
}
