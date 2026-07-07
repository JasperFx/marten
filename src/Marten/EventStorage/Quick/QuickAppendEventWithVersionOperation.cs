#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Core;
using Weasel.Storage;

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
    private readonly IStorageDialect _dialect;

    public QuickAppendEventWithVersionOperation(
        string appendEventSqlPrefix,
        string appendEventSqlSuffix,
        IEventMetadataBinder[] metadataBinders,
        bool isGuidStreamIdentity,
        System.Func<IEvent, string> serializeEventData,
        System.Func<IEvent, byte[]?> serializeEventBdata,
        IStorageDialect dialect,
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
        _dialect = dialect;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append(_appendEventSqlPrefix);

        var dialect = _dialect;
        IGroupedParameterBuilder pb = builder.CreateGroupedParameterBuilder(',');

        // Core columns — same order + types as RichAppendEventOperation.
        // Provider parameter types come from the dialect so the op carries no
        // direct Npgsql reference.
        dialect.SetParameterType(pb.AppendParameter(_serializeEventData(Event)), StorageColumnType.Json);
        dialect.SetParameterType(pb.AppendParameter(Event.EventTypeName), StorageColumnType.String);
        dialect.SetParameterType(pb.AppendParameter(Event.DotNetTypeName), StorageColumnType.String);

        // #4515: bdata bytea (nullable). NULL for JSON-serialized events;
        // bytes for binary-serialized events. Pinned at SELECT position 3
        // (right after mt_dotnet_type) by EventsTable.SelectColumns, so the
        // bind sequence here mirrors that position. The neutral AppendParameter
        // writes a null byte[] as DBNull.
        var bdataBytes = _serializeEventBdata(Event);
        dialect.SetParameterType(pb.AppendParameter(bdataBytes), StorageColumnType.Binary);

        dialect.SetParameterType(pb.AppendParameter(Event.Id), StorageColumnType.Guid);

        if (_isGuidStreamIdentity)
            dialect.SetParameterType(pb.AppendParameter(Stream.Id), StorageColumnType.Guid);
        else
            dialect.SetParameterType(pb.AppendParameter(Stream.Key), StorageColumnType.String);

        dialect.SetParameterType(pb.AppendParameter(Event.Version), StorageColumnType.Long);
        dialect.SetParameterType(pb.AppendParameter(Event.Timestamp), StorageColumnType.Timestamp);
        dialect.SetParameterType(pb.AppendParameter(Stream.TenantId), StorageColumnType.String);

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
