using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events.Aggregation;

internal class TenantSliceRange<TDoc, TId>: EventRangeGroup
{
    private readonly IAggregationRuntime<TDoc, TId> _runtime;
    private readonly DocumentStore _store;

    public TenantSliceRange(DocumentStore store, IAggregationRuntime<TDoc, TId> runtime, EventRange range,
        IReadOnlyList<TenantSliceGroup<TDoc, TId>> groups, CancellationToken projectionCancellation): base(range,
        projectionCancellation)
    {
        _store = store;
        _runtime = runtime;
        Groups = groups;
    }

    public IReadOnlyList<TenantSliceGroup<TDoc, TId>> Groups { get; private set; }


    protected override void reset()
    {
        foreach (var group in Groups) group.Reset();
    }

    public override void Dispose()
    {
        foreach (var group in Groups) group.Dispose();
    }

    public override string ToString()
    {
        return $"Aggregate for {Range}, {Groups.Count} slices";
    }

    public override async Task ConfigureUpdateBatch(ProjectionUpdateBatch batch)
    {
        await Parallel.ForEachAsync(Groups, CancellationToken.None,
                async (group, _) =>
                    await group.Start(batch, _runtime, _store, this).ConfigureAwait(false))
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
