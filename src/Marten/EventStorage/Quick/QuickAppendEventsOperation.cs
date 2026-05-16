#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.Quick;

/// <summary>
/// Closed-shape <see cref="QuickAppendEventsOperationBase"/> for the Quick
/// (batched) append path. Calls the <c>mt_quick_append_events</c> server
/// function with one <see cref="NpgsqlTypes.NpgsqlDbType.Array"/> parameter
/// per column, carrying one value per event in the stream. The base
/// class's <c>Postprocess</c> walks the returned long[] and assigns
/// server-generated versions + sequences back onto each event.
/// </summary>
/// <remarks>
/// <para>
/// W4 #4414. Source-gen (W5) emits this exact pattern per
/// <c>(stream-identity, metadata-flags, tag-types)</c> tuple — the
/// closed-shape v9 hand-write uses descriptor flags + the existing
/// protected helpers on <see cref="QuickAppendEventsOperationBase"/>
/// (<c>writeId</c> / <c>writeKey</c> / <c>writeBasicParameters</c> /
/// <c>writeCausationIds</c> / etc.) so the per-batch column rentals +
/// Postprocess sequence assignment carry over unchanged.
/// </para>
/// <para>
/// The Events property gets populated by <c>QuickEventAppender</c> after
/// construction so the base's Postprocess can check
/// <see cref="EventGraph.UseMandatoryStreamTypeDeclaration"/>.
/// </para>
/// </remarks>
internal sealed class QuickAppendEventsOperation: QuickAppendEventsOperationBase
{
    private readonly QuickEventStorageDescriptor _descriptor;

    public QuickAppendEventsOperation(QuickEventStorageDescriptor descriptor, StreamAction stream)
        : base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append(_descriptor.QuickAppendEventsSql);

        var pb = builder.CreateGroupedParameterBuilder(',');

        // Stream identifier — first positional arg of mt_quick_append_events.
        if (_descriptor.IsGuidStreamIdentity)
            writeId(pb);
        else
            writeKey(pb);

        // Always-on: aggregate type, tenant id, event_ids[], event_types[],
        // dotnet_types[], bodies[]. Bodies are serialized via the session
        // serializer to a sized UTF-8 byte[] (no intermediate string alloc).
        writeBasicParameters(pb, session);

        // Configuration-gated metadata column arrays. Order MUST match the
        // dialect's QuickAppendEventFunction.WriteCreateStatement column
        // ordering: causation_ids, correlation_ids, headers, user_names,
        // [timestamps in the server-timestamps variant], tag arrays.
        if (_descriptor.HasCausationId) writeCausationIds(pb);
        if (_descriptor.HasCorrelationId) writeCorrelationIds(pb);
        if (_descriptor.HasHeaders) writeHeaders(pb, session);
        if (_descriptor.HasUserName) writeUserNames(pb, session);

        // DCB tag-array writes (non-HStore mode). Each registered TagType
        // contributes one varchar[] parameter, in TagTypes order. The base
        // helper handles the iteration off Events.TagTypes (set by the
        // appender), so the operation just calls the helper when the
        // dialect flagged this configuration as needing tag writes.
        if (_descriptor.HasTagWrites) writeAllTagValues(pb);

        builder.Append(")");
    }
}
