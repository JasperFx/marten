#nullable enable
using System;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage.Quick;

/// <summary>
/// <see cref="EventStorage{TId}"/> for <c>EventAppendMode.Quick</c> — batch
/// append via the <c>mt_quick_append_events</c> server function with array
/// parameters covering every event in the stream. RETURNING-array
/// read-back assigns server-generated versions + sequences onto the
/// events list.
/// </summary>
internal sealed class QuickEventStorage<TId>: EventStorage<TId>
{
    private readonly QuickEventStorageDescriptor _descriptor;

    public QuickEventStorage(QuickEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IStorageOperation AppendEvent(IMartenSession session, StreamAction stream, IEvent @event)
        => throw new NotSupportedException(
            $"{nameof(QuickEventStorage<TId>)} batches events per stream — single-event appends route through " +
            $"{nameof(QuickAppendEvents)}.");

    public override IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent @event)
        => throw new NotSupportedException(
            $"{nameof(QuickEventStorage<TId>)} doesn't support per-event QuickWithVersion. " +
            $"Use AppendMode = Full + RichEventStorage for that path.");

    public override IStorageOperation QuickAppendEvents(StreamAction stream)
        => new QuickAppendEventsOperation(_descriptor, stream);

    public override IStorageOperation InsertStream(StreamAction stream)
        => throw new NotImplementedException("InsertStream operation lands in the next iteration.");

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => throw new NotImplementedException("UpdateStreamVersion operation lands in the next iteration.");

    public override IQueryHandler<StreamState> QueryForStream(StreamAction stream)
        => throw new NotImplementedException("StreamStateQueryHandler lands in the next iteration.");
}
