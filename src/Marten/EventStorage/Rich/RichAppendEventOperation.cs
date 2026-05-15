#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Rich;

/// <summary>
/// Hand-written example of what <c>Marten.SourceGenerator</c> would emit
/// for the Rich (full-mode) per-event append operation in an
/// <see cref="EventGraph"/> configured with Guid stream identity + the
/// default core schema. The metadata-column slice is delegated to the
/// descriptor's <see cref="IEventMetadataBinder"/> array; only this
/// concrete class's <i>core</i> column writes are inlined.
/// </summary>
/// <remarks>
/// <para>
/// W4 spike (#4404). Once W5 (Marten.SourceGenerator) lands, this file
/// goes away and the generator emits this exact pattern per
/// <c>(core column set, identity, dialect)</c> tuple — a small fixed
/// matrix that doesn't expand when configuration axes flip on or off,
/// because the metadata variability lives on the binder array.
/// </para>
/// <para>
/// <b>Hot-path budget:</b> one virtual <see cref="ConfigureCommand"/>
/// call, two <see cref="ICommandBuilder.Append(string)"/> calls,
/// seven inlined <see cref="IGroupedParameterBuilder.AppendParameter"/>
/// calls for the core columns, plus one virtual call per metadata
/// binder in the descriptor's array (typically 1–4). No runtime
/// branching on the core schema; metadata variability is resolved at
/// startup when the descriptor builder picks which binders go in the
/// array.
/// </para>
/// </remarks>
internal sealed class RichAppendEventOperation: AppendEventOperationBase
{
    private readonly RichEventStorageDescriptor _descriptor;

    public RichAppendEventOperation(RichEventStorageDescriptor descriptor, StreamAction stream, IEvent e)
        : base(stream, e)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append(_descriptor.AppendEventSqlPrefix);

        var pb = builder.CreateGroupedParameterBuilder(',');

        // --- Inlined core column writes ---
        // Always present, no configuration variance, no read-back. Source-gen
        // emits these statements verbatim into the concrete class. Order
        // matches the core slice of the SQL prefix's column list.

        pb.AppendParameter(Event.Id, NpgsqlDbType.Uuid);
        pb.AppendParameter(Event.Version, NpgsqlDbType.Bigint);
        pb.AppendParameter(_descriptor.SerializeEventData(Event), NpgsqlDbType.Jsonb);
        pb.AppendParameter(Event.EventTypeName, NpgsqlDbType.Varchar);
        pb.AppendParameter(session.TenantId, NpgsqlDbType.Varchar);
        pb.AppendParameter(Event.DotNetTypeName, NpgsqlDbType.Varchar);

        // Stream id is the one core-column variant — Guid identity uses
        // Stream.Id, string identity uses Stream.Key. Source-gen emits two
        // closed-shape concrete classes (one per identity) so the hot path
        // stays branch-free.
        pb.AppendParameter(Stream.Id, NpgsqlDbType.Uuid);

        // --- Metadata column writes via the binder array ---
        // Variability axis lives here. Each active binder writes one
        // parameter (or contributes a server-side constant via ValueSql).
        // Order in the array matches the metadata slice of the SQL prefix's
        // column list — descriptor builder owns both sides.
        var binders = _descriptor.MetadataBinders;
        for (var i = 0; i < binders.Length; i++)
        {
            binders[i].Bind(pb, Stream, Event, session);
        }

        builder.Append(_descriptor.AppendEventSqlSuffix);
    }

    // Postprocess: no override needed. Rich mode has no RETURNING clause —
    // nothing to read back. The base class's no-op suffices. Compare with
    // QuickAppendEventsOperation.Postprocess which walks a returned long[]
    // and assigns versions + sequences onto the events list.
}
