#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Quick;

/// <summary>
/// Hand-written example of what <c>Marten.SourceGenerator</c> would emit
/// for the Quick batch-append operation in an <see cref="EventGraph"/>
/// configured with Guid stream identity + the default core schema. Calls
/// the <c>mt_quick_append_events</c> server function with one
/// <c>NpgsqlDbType.Array</c> parameter per column, carrying one value per
/// event in the stream. RETURNING-array postprocess walks the events
/// list backwards to assign the server-generated version + sequence
/// numbers.
/// </summary>
/// <remarks>
/// <para>
/// W4 spike (#4404). Subclasses the existing
/// <see cref="QuickAppendEventsOperationBase"/> — same as today's codegen
/// path, but the body is hand-written by source-gen instead of emitted
/// by Roslyn at first use.
/// </para>
/// <para>
/// <b>Different shape from Rich:</b> bind cost is per-column not
/// per-event, post-processing carries the version+sequence write-back
/// that Rich doesn't need, and configuration axes (headers / causation /
/// correlation / username) show up as <i>inlined bind sequences in this
/// concrete class</i> rather than as descriptor binder-array contents.
/// That's the reason Quick gets a separate <c>QuickEventStorage</c>
/// instead of trying to unify with Rich via an interface — the divergence
/// is fundamental, not cosmetic.
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

        // --- Stream identifier (one scalar parameter) ---
        // Guid identity. The string-identity source-gen variant uses
        // writeKey(pb) with NpgsqlDbType.Varchar instead.
        pb.AppendParameter(Stream.Id, NpgsqlDbType.Uuid);

        // --- Core column array parameters ---
        // One NpgsqlDbType.Array parameter per column, each containing
        // one value per event in the stream. Hand-written by source-gen,
        // exactly the bind sequence today's QuickAppendEventsOperationBase's
        // protected writeBasicParameters helper produces — just inlined
        // into the concrete class instead of called through a virtual.
        var events = Stream.Events;
        var count = events.Count;

        // tenant (every event gets the same value but the array shape
        // matches the function signature)
        var tenants = new string?[count];
        var types = new string?[count];
        var versions = new long[count];
        var dotnetTypes = new string?[count];
        var datas = new string?[count];
        for (var i = 0; i < count; i++)
        {
            var e = events[i];
            tenants[i] = session.TenantId;
            types[i] = e.EventTypeName;
            versions[i] = e.Version;
            dotnetTypes[i] = e.DotNetTypeName;
            datas[i] = _descriptor.SerializeEventData(e);
        }

        pb.AppendParameter(tenants).NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        pb.AppendParameter(types).NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        pb.AppendParameter(versions).NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bigint;
        pb.AppendParameter(dotnetTypes).NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        pb.AppendParameter(datas).NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;

        // --- Configuration-gated metadata column arrays ---
        // Source-gen emits these inlined per active column when the
        // consumer's StoreOptions.Events has the corresponding axis
        // enabled. Today's QuickAppendEventsOperationBase exposes them
        // as protected helpers (writeCausationIds, writeCorrelationIds,
        // writeHeaders, writeUserNames); source-gen output inlines
        // their bodies into the ConfigureCommand instead of dispatching
        // through the base.
        //
        // For this default-config sample, none of the optional metadata
        // axes are on, so no further binds. When headers are enabled
        // the generator would emit:
        //
        //     var headers = new string?[count];
        //     for (var i = 0; i < count; i++)
        //         headers[i] = events[i].Headers == null ? null : session.Serializer.ToJson(events[i].Headers);
        //     pb.AppendParameter(headers).NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
        //
        // …and likewise for the other axes. The shape is uniform enough that
        // hand-inlining beats a per-binder dispatch (see SPIKE.md).
    }

    // Postprocess: hand-written read-back. The mt_quick_append_events
    // function returns long[] where values[0] is the final version
    // assigned + values[1..] are the per-event sequence numbers in order.
    // Walk the events list backwards to assign versions; walk forward
    // to assign sequences.
    //
    // This is exactly the existing QuickAppendEventsOperationBase.Postprocess
    // body — the W4 spike keeps it on the concrete subclass (source-gen
    // emits it) rather than the abstract base, so the read-back semantics
    // for the QuickWithServerTimestamps variant can diverge cleanly when
    // its returned array shape differs.
    //
    // Spike sample doesn't override Postprocess — the base's existing
    // implementation works as-is for the default config. The override
    // becomes necessary when a future server-set metadata column needs
    // to be read back AND the existing base impl doesn't already cover it.
}
