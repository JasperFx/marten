#nullable enable
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage.Rich;

/// <summary>
/// <see cref="EventStorage{TId}"/> implementation for the full-mode (Rich)
/// append flow. Yields one <see cref="IStorageOperation"/> per event in
/// the stream — each operation is a per-row INSERT into <c>mt_events</c>
/// with the metadata columns bound via the descriptor's binder array.
/// </summary>
/// <remarks>
/// Spike scope (#4404 W4): the operation factories return placeholder
/// instances pulled from <see cref="RichAppendEventOperation"/> — see that
/// class for the hand-written sample of source-gen output. The
/// <c>InsertStream</c> / <c>UpdateStreamVersion</c> / <c>StreamStateQueryHandler</c>
/// methods are stubbed; they'd return concrete subclasses of the
/// existing <c>InsertStreamBase</c> / <c>UpdateStreamVersion</c> /
/// <c>StreamStateSelector</c> bases when the spike is extended.
/// </remarks>
internal sealed class RichEventStorage<TId>: EventStorage<TId>
{
    private readonly RichEventStorageDescriptor _descriptor;

    public RichEventStorage(RichEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IEnumerable<IStorageOperation> AppendStreamEvents(StreamAction stream)
    {
        // Rich = one operation per event. The source-gen-emitted
        // RichAppendEventOperation handles each event's INSERT.
        foreach (var e in stream.Events)
        {
            yield return new RichAppendEventOperation(_descriptor, stream, e);
        }
    }

    public override IStorageOperation InsertStream(StreamAction stream)
        => throw new System.NotImplementedException("Spike scope — InsertStream operation belongs to the next spike iteration.");

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => throw new System.NotImplementedException("Spike scope — UpdateStreamVersion operation belongs to the next spike iteration.");

    public override IQueryHandler<StreamState?> StreamStateQueryHandler(TId streamId)
        => throw new System.NotImplementedException("Spike scope — StreamStateQueryHandler belongs to the next spike iteration.");
}
