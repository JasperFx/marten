#nullable enable
using System;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// <see cref="EventStorage{TId}"/> for
/// <c>EventAppendMode.QuickWithServerTimestamps</c>. Same shape as
/// <see cref="Quick.QuickEventStorage{TId}"/> + the server-side <c>now()</c>
/// timestamp array; the returned timestamps get written back onto each
/// event in the operation's <c>Postprocess</c>.
/// </summary>
internal sealed class QuickWithServerTimestampsEventStorage<TId>: EventStorage<TId>
{
    private readonly QuickWithServerTimestampsEventStorageDescriptor _descriptor;

    public QuickWithServerTimestampsEventStorage(QuickWithServerTimestampsEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IStorageOperation AppendEvent(IMartenSession session, StreamAction stream, IEvent @event)
        => throw new NotSupportedException(
            $"{nameof(QuickWithServerTimestampsEventStorage<TId>)} batches events per stream — single-event appends route through " +
            $"{nameof(QuickAppendEvents)}.");

    public override IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent @event)
        => throw new NotSupportedException(
            $"{nameof(QuickWithServerTimestampsEventStorage<TId>)} doesn't support per-event QuickWithVersion. " +
            $"Use AppendMode = Full + RichEventStorage for that path.");

    public override IStorageOperation QuickAppendEvents(StreamAction stream)
        => new QuickAppendEventsWithServerTimestampsOperation(_descriptor, stream);

    public override IStorageOperation InsertStream(StreamAction stream)
        => throw new NotImplementedException("InsertStream operation lands in the next iteration.");

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => throw new NotImplementedException("UpdateStreamVersion operation lands in the next iteration.");

    public override IQueryHandler<StreamState> QueryForStream(StreamAction stream)
        => throw new NotImplementedException("StreamStateQueryHandler lands in the next iteration.");
}
