#nullable enable
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage.Quick;

/// <summary>
/// <see cref="EventStorage{TId}"/> implementation for the Quick batch
/// append flow. Yields a single <see cref="IStorageOperation"/> per
/// <see cref="StreamAction"/> — the batched call into the
/// <c>mt_quick_append_events</c> server function with per-column array
/// parameters covering every event in the stream.
/// </summary>
/// <remarks>
/// Different shape from <c>RichEventStorage</c>: one batched operation
/// instead of N per-event operations, RETURNING-array read-back to write
/// versions + sequences onto the events list, per-batch array parameter
/// binding instead of per-event scalars. The two storage classes share
/// nothing on the append path — the "completely different implementations"
/// framing in SPIKE.md is the rationale.
/// </remarks>
internal sealed class QuickEventStorage<TId>: EventStorage<TId>
{
    private readonly QuickEventStorageDescriptor _descriptor;

    public QuickEventStorage(QuickEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IEnumerable<IStorageOperation> AppendStreamEvents(StreamAction stream)
    {
        // Quick = one operation per stream, regardless of event count.
        yield return new QuickAppendEventsOperation(_descriptor, stream);
    }

    public override IStorageOperation InsertStream(StreamAction stream)
        => throw new System.NotImplementedException("Spike scope — InsertStream belongs to the next spike iteration.");

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => throw new System.NotImplementedException("Spike scope — UpdateStreamVersion belongs to the next spike iteration.");

    public override IQueryHandler<StreamState?> StreamStateQueryHandler(TId streamId)
        => throw new System.NotImplementedException("Spike scope — StreamStateQueryHandler belongs to the next spike iteration.");
}
