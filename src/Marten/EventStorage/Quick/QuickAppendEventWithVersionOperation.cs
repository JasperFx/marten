#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Quick;

/// <summary>
/// Closed-shape per-event INSERT operation for the Quick paths' "with
/// version" branch. The Quick appender uses this shape — one INSERT per
/// event with a pre-assigned <see cref="IEvent.Version"/> — when:
/// <list type="bullet">
///   <item>A stream is starting (no prior version to fetch).</item>
///   <item>An existing stream has <see cref="StreamAction.ExpectedVersionOnServer"/>
///         set (optimistic concurrency).</item>
/// </list>
/// Other cases use the bulk <c>mt_quick_append_events</c> function call
/// via <see cref="QuickAppendEventsOperation"/>.
/// </summary>
/// <remarks>
/// <para>
/// Same INSERT shape as <c>Rich.RichAppendEventOperation</c>, with one
/// divergence: seq_id is a server-side <c>nextval(...)</c> SQL literal
/// (baked into the descriptor's <see cref="QuickEventStorageDescriptor.AppendEventSqlSuffix"/>)
/// instead of a bound parameter. The Quick appender pre-fetches no
/// sequences (<c>_unusedSequencesSentinel</c>) — sequences come from the
/// server.
/// </para>
/// <para>
/// Used by both <see cref="QuickEventStorage{TId}"/> and
/// <c>QuickWithServerTimestampsEventStorage&lt;TId&gt;</c>. The
/// server-timestamps variant doesn't change the per-event INSERT path —
/// it only changes the bulk-function call's signature.
/// </para>
/// </remarks>
internal sealed class QuickAppendEventWithVersionOperation: AppendEventOperationBase
{
    private readonly string _appendEventSqlPrefix;
    private readonly string _appendEventSqlSuffix;
    private readonly IEventMetadataBinder[] _metadataBinders;
    private readonly bool _isGuidStreamIdentity;
    private readonly System.Func<IEvent, string> _serializeEventData;
    private readonly System.Func<IEvent, byte[]?> _serializeEventBdata;

    public QuickAppendEventWithVersionOperation(
        string appendEventSqlPrefix,
        string appendEventSqlSuffix,
        IEventMetadataBinder[] metadataBinders,
        bool isGuidStreamIdentity,
        System.Func<IEvent, string> serializeEventData,
        System.Func<IEvent, byte[]?> serializeEventBdata,
        StreamAction stream,
        IEvent e)
        : base(stream, e)
    {
        _appendEventSqlPrefix = appendEventSqlPrefix;
        _appendEventSqlSuffix = appendEventSqlSuffix;
        _metadataBinders = metadataBinders;
        _isGuidStreamIdentity = isGuidStreamIdentity;
        _serializeEventData = serializeEventData;
        _serializeEventBdata = serializeEventBdata;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append(_appendEventSqlPrefix);

        var pb = builder.CreateGroupedParameterBuilder(',');

        // Core columns — same order + types as RichAppendEventOperation.
        pb.AppendParameter(_serializeEventData(Event), NpgsqlDbType.Jsonb);
        pb.AppendParameter(Event.EventTypeName, NpgsqlDbType.Varchar);
        pb.AppendParameter(Event.DotNetTypeName, NpgsqlDbType.Varchar);

        // #4515: bdata bytea (nullable). NULL for JSON-serialized events;
        // bytes for binary-serialized events. Pinned at SELECT position 3
        // (right after mt_dotnet_type) by EventsTable.SelectColumns, so the
        // bind sequence here mirrors that position.
        var bdataBytes = _serializeEventBdata(Event);
        pb.AppendParameter(bdataBytes ?? (object)System.DBNull.Value, NpgsqlDbType.Bytea);

        pb.AppendParameter(Event.Id, NpgsqlDbType.Uuid);

        if (_isGuidStreamIdentity)
            pb.AppendParameter(Stream.Id, NpgsqlDbType.Uuid);
        else
            pb.AppendParameter(Stream.Key, NpgsqlDbType.Varchar);

        pb.AppendParameter(Event.Version, NpgsqlDbType.Bigint);
        pb.AppendParameter(Event.Timestamp, NpgsqlDbType.TimestampTz);
        pb.AppendParameter(Stream.TenantId, NpgsqlDbType.Varchar);

        // Optional metadata binders (causation / correlation / headers /
        // user_name). The dialect's filtered list excludes SequenceColumnBinder
        // — seq_id is server-set via nextval() literal in the suffix.
        for (var i = 0; i < _metadataBinders.Length; i++)
        {
            _metadataBinders[i].Bind(pb, Stream, Event, session);
        }

        // Suffix carries the `, nextval('schema.mt_events_sequence'))`
        // server-side fragment + closing paren.
        builder.Append(_appendEventSqlSuffix);
    }
}
