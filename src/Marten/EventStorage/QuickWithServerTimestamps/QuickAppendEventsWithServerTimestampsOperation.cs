#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// Closed-shape <see cref="QuickAppendEventsOperationBase"/> for the
/// <c>QuickWithServerTimestamps</c> append path. Adds one extra parameter
/// to the <c>mt_quick_append_events</c> call — the per-event timestamp
/// array — and otherwise binds the same column arrays as
/// <see cref="Quick.QuickAppendEventsOperation"/>.
/// </summary>
/// <remarks>
/// <para>
/// W4 #4415. Despite the name, the client sends the timestamps (from
/// <see cref="IEvent.Timestamp"/>) — the function uses
/// <c>timestamps[index]</c> when this mode is on, vs the default Quick
/// path's server-side <c>now() at time zone 'utc'</c>. See
/// <c>QuickAppendEventFunction.WriteCreateStatement</c>.
/// </para>
/// </remarks>
internal sealed class QuickAppendEventsWithServerTimestampsOperation: QuickAppendEventsOperationBase
{
    private readonly QuickWithServerTimestampsEventStorageDescriptor _descriptor;

    public QuickAppendEventsWithServerTimestampsOperation(
        QuickWithServerTimestampsEventStorageDescriptor descriptor, StreamAction stream)
        : base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append(_descriptor.QuickAppendEventsWithServerTimestampsSql);

        var pb = builder.CreateGroupedParameterBuilder(',');

        if (_descriptor.IsGuidStreamIdentity)
            writeId(pb);
        else
            writeKey(pb);

        // #4515 Phase 2: SerializeEventBdata gives the binary bytes per event
        // (null for JSON events). Appends a bdatas bytea[] parameter right
        // after bodies in the function call.
        writeBasicParameters(pb, session, _descriptor.SerializeEventBdata);

        // Order MUST match the dialect's function-signature ordering:
        // metadata columns first, then timestamps, then tag arrays.
        // See QuickAppendEventFunction.WriteCreateStatement.
        if (_descriptor.HasCausationId) writeCausationIds(pb);
        if (_descriptor.HasCorrelationId) writeCorrelationIds(pb);
        if (_descriptor.HasHeaders) writeHeaders(pb, session);
        if (_descriptor.HasUserName) writeUserNames(pb, session);

        // The one divergence from the no-server-timestamps Quick path:
        // the per-event timestamp array.
        writeTimestamps(pb);

        if (_descriptor.HasTagWrites) writeAllTagValues(pb);

        builder.Append(")");
    }
}
