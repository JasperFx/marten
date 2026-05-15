#nullable enable
using System.Data.Common;
using JasperFx.Events;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.Metadata;

/// <summary>
/// Sample <see cref="IEventMetadataBinder"/> for the server-assigned
/// <c>seq_id</c> column. Demonstrates the round-trip case the metadata
/// binder seam was added to support: <see cref="Bind"/> is a no-op
/// (the value comes from <c>nextval()</c> on the server, not from the
/// client) while <see cref="OnRead"/> writes the returned sequence number
/// back onto the <see cref="IEvent.Sequence"/> property so projection +
/// async-daemon code can read it later in the same session.
/// </summary>
/// <remarks>
/// <para>
/// This is the "call back and set values on the document itself after we
/// get results" path. The closed-shape source-gen-emitted operation can't
/// cleanly inline this — the read-back happens against a <see cref="DbDataReader"/>
/// that not every operation kind has (full-mode append has no RETURNING
/// clause; quick-append modes return a sequence array). The binder lets us
/// express the round-trip uniformly and have the operation invoke it
/// when (and only when) the operation's SQL actually returns rows.
/// </para>
/// <para>
/// Mirrors the read-back loop in
/// <c>QuickAppendEventsOperationBase.Postprocess</c> today, just split out
/// per-binder so each metadata column owns its own write-back logic.
/// </para>
/// </remarks>
internal sealed class SequenceColumnBinder: IEventMetadataBinder
{
    public string ColumnName => "seq_id";

    // Server-assigned via nextval(). The closed-shape operation's SQL
    // includes this fragment literally in the VALUES list; no parameter
    // gets bound by Bind.
    public string ValueSql => "nextval('mt_events_sequence')";

    public void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IMartenSession session)
    {
        // No-op. ValueSql is a server-side expression — nothing to bind.
    }

    public void OnRead(DbDataReader reader, int columnOrdinal, StreamAction stream, IEvent @event)
    {
        @event.Sequence = reader.GetInt64(columnOrdinal);
    }
}
