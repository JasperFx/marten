#nullable enable
using JasperFx.Events;
using Marten.Internal;
using Weasel.Core;
using Weasel.Storage;

namespace Marten.EventStorage.Metadata;

/// <summary>
/// <see cref="IEventMetadataBinder"/> for the <c>seq_id</c> column on the
/// Rich (full-mode) append path. Binds <see cref="IEvent.Sequence"/> as a
/// bigint parameter — <c>RichEventAppender</c> pre-fetches a queue of
/// sequence numbers via <c>EventSequenceFetcher</c> and assigns them to
/// each event before <c>storage.AppendEvent(...)</c> is called, so the
/// value is already populated on <see cref="IEvent.Sequence"/> by the time
/// this binder runs.
/// </summary>
/// <remarks>
/// <para>
/// QuickWithVersion mode (not yet covered by the closed-shape hierarchy —
/// <see cref="Rich.RichEventStorage{TId}"/>.QuickAppendEventWithVersion still
/// throws) takes a different shape: <c>nextval('mt_events_sequence')</c>
/// as a SQL literal in the VALUES list, no parameter bind, plus a
/// read-back from a RETURNING clause. When that path lands, a separate
/// <c>SequenceServerSideBinder</c> (or a mode flag here) will model it —
/// the binder seam was originally added to demonstrate exactly that
/// "server-set, read-back" round-trip.
/// </para>
/// </remarks>
internal sealed class SequenceColumnBinder: IEventMetadataBinder
{
    private readonly IStorageDialect _dialect;

    public SequenceColumnBinder(IStorageDialect dialect)
    {
        _dialect = dialect;
    }

    public string ColumnName => "seq_id";
    public string ValueSql => "?";

    public void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IStorageSession session)
    {
        var parameter = pb.AppendParameter(@event.Sequence);
        _dialect.SetParameterType(parameter, StorageColumnType.Long);
    }
}
