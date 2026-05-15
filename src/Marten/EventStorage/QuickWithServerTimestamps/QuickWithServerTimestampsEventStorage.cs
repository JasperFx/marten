#nullable enable
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// <see cref="EventStorage{TId}"/> implementation for
/// <c>EventAppendMode.QuickWithServerTimestamps</c>. Same shape as
/// <see cref="Quick.QuickEventStorage{TId}"/> — one batched operation
/// per stream — but the operation includes the server-side <c>now()</c>
/// timestamp array as an extra parameter and writes the returned
/// timestamps back onto the events.
/// </summary>
internal sealed class QuickWithServerTimestampsEventStorage<TId>: EventStorage<TId>
{
    private readonly QuickWithServerTimestampsEventStorageDescriptor _descriptor;

    public QuickWithServerTimestampsEventStorage(QuickWithServerTimestampsEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IEnumerable<IStorageOperation> AppendStreamEvents(StreamAction stream)
    {
        yield return new QuickAppendEventsWithServerTimestampsOperation(_descriptor, stream);
    }

    public override IStorageOperation InsertStream(StreamAction stream)
        => throw new System.NotImplementedException("Spike scope — InsertStream belongs to the next spike iteration.");

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => throw new System.NotImplementedException("Spike scope — UpdateStreamVersion belongs to the next spike iteration.");

    public override IQueryHandler<StreamState?> StreamStateQueryHandler(TId streamId)
        => throw new System.NotImplementedException("Spike scope — StreamStateQueryHandler belongs to the next spike iteration.");
}
