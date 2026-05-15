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

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// Hand-written example of what <c>Marten.SourceGenerator</c> would emit
/// for the <c>QuickWithServerTimestamps</c> append operation. Adds one
/// extra array parameter to <see cref="Quick.QuickAppendEventsOperation"/>:
/// a Postgres-side <c>now()</c>-filled <c>timestamp with time zone[]</c>
/// that the <c>mt_quick_append_events</c> server function copies into the
/// <c>mt_events.timestamp</c> column AND returns alongside the version +
/// sequence array.
/// </summary>
/// <remarks>
/// <para>
/// W4 spike (#4404). Subclasses the existing
/// <see cref="QuickAppendEventsOperationBase"/> just like
/// <see cref="Quick.QuickAppendEventsOperation"/> does — the divergence
/// is in the ConfigureCommand body's extra array parameter and the
/// Postprocess override that reads back the server-assigned timestamps.
/// </para>
/// <para>
/// The W4 framing of "completely different IEventStorage implementations"
/// is what makes this clean. The QuickWithServerTimestamps storage class
/// doesn't share code with the no-server-timestamps Quick storage class
/// beyond the abstract base — instead of carrying a configuration flag
/// in the descriptor and branching on it per call, the choice of
/// concrete storage class IS the configuration. Set at startup, branch-
/// free thereafter.
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

        pb.AppendParameter(Stream.Id, NpgsqlDbType.Uuid);

        // Core column arrays — same as Quick. The single divergence from
        // QuickAppendEventsOperation is the extra timestamp-array parameter
        // at the end (and the SQL function signature accepts it).
        var events = Stream.Events;
        var count = events.Count;

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

        // --- Server-timestamps divergence ---
        // The server function accepts an array of `now()` placeholders so
        // it can stamp all rows with the same per-batch timestamp on the
        // server clock. The client doesn't pre-compute timestamps —
        // instead it sends an array of NULLs that the function replaces
        // with `now()` and returns. See SPIKE.md "Server-timestamps
        // divergence" for why this is one of the three closed-shape
        // variants instead of a configuration flag.
        var serverTimestamps = new DateTime?[count];  // all null — server fills
        pb.AppendParameter(serverTimestamps).NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz;
    }

    // Postprocess: read back BOTH the version+sequence array (same as
    // Quick) AND the timestamp array. The base class's Postprocess walks
    // the version+sequence array; this override extends it to read the
    // timestamp array and assign onto each event's Timestamp property.
    //
    // Spike doesn't fill this in — the override would have to coordinate
    // with the base's existing Postprocess logic, and the existing
    // QuickAppendEventsOperationBase already covers the
    // server-timestamps branch via a hand-written conditional today.
    // Splitting that out cleanly is part of extending the spike; the
    // shape is just:
    //
    //     public override void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    //     {
    //         base.Postprocess(reader, exceptions);  // walks version+sequence
    //         if (reader.NextResult() && reader.Read())
    //         {
    //             var timestamps = reader.GetFieldValue<DateTimeOffset[]>(0);
    //             for (var i = 0; i < timestamps.Length; i++)
    //                 Stream.Events[i].Timestamp = timestamps[i];
    //         }
    //     }
    //
    // (Pseudocode — depends on how the server function shapes its
    // multi-result-set returns. Today it's one array; the W4 split lets
    // QuickWithServerTimestamps diverge to a different shape without
    // forcing per-call branches into the Quick path.)
}
