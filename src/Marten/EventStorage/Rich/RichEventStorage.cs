#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage.Rich;

/// <summary>
/// <see cref="EventStorage{TId}"/> for the Rich (per-event) append paths —
/// covers both <c>EventAppendMode.Full</c> and
/// <c>EventAppendMode.QuickWithVersion</c>. Yields one
/// <see cref="IStorageOperation"/> per event. The two paths differ only in
/// whether the event's version is pre-assigned (QuickWithVersion) or
/// computed from a stream-state lookup (Full); both bind per-row scalar
/// parameters.
/// </summary>
internal sealed class RichEventStorage<TId>: EventStorage<TId>
{
    private readonly RichEventStorageDescriptor _descriptor;

    public RichEventStorage(RichEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IStorageOperation AppendEvent(IMartenSession session, StreamAction stream, IEvent @event)
        => new RichAppendEventOperation(_descriptor, stream, @event);

    public override IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent @event)
        => throw new NotImplementedException("RichAppendEventQuickWithVersionOperation lands in the next iteration.");

    public override IStorageOperation QuickAppendEvents(StreamAction stream)
        => throw new NotSupportedException(
            $"{nameof(RichEventStorage<TId>)} doesn't support batch quick-append. " +
            $"Set StoreOptions.Events.AppendMode to Quick or QuickWithServerTimestamps to use a batched storage class.");

    public override IStorageOperation InsertStream(StreamAction stream)
        => throw new NotImplementedException("InsertStream operation lands in the next iteration.");

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => throw new NotImplementedException("UpdateStreamVersion operation lands in the next iteration.");

    public override IQueryHandler<StreamState> QueryForStream(StreamAction stream)
        => throw new NotImplementedException("StreamStateQueryHandler lands in the next iteration.");

    internal override IReadOnlyList<IEventTableColumn> ReaderColumns => _descriptor.ReaderColumns;
}
