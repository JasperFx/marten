#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using JasperFx.Events.Tags;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

/// <summary>
/// The WHERE predicate that selects the events a DCB tag query matches when tags live in the per-type
/// <c>mt_event_tag_*</c> tables (<see cref="DcbStorageMode.TagTables"/>). Shared by
/// <see cref="FetchForWritingByTagsHandler{T}"/> and <see cref="QueryEventStore"/>'s tag query, so the two
/// select the same events.
/// </summary>
/// <remarks>
/// #5897: each condition is an index probe into its own tag table, and the probes are combined on
/// <c>seq_id</c>:
/// <code>
/// e.seq_id in (select t.seq_id from mt_event_tag_a t where t.value = $1
///              union all
///              select t.seq_id from mt_event_tag_b t join mt_events x on x.seq_id = t.seq_id
///                     where t.value = $2 and x.type = $3)
/// </code>
/// This used to LEFT JOIN every tag table the query named onto <c>mt_events</c> and put the OR above
/// the joins. PostgreSQL cannot drive an OR across two outer-joined tables from their indexes, so a
/// query spanning two tag types hash-joined the whole of <c>mt_events</c> with both tag tables: 328 ms
/// at 1M events to return 1,010 rows, against 7 ms for this shape. The join also returned an event once
/// per matching tag row, so an event carrying two values of one tag type that the query both named was
/// read, and folded, twice; <c>in</c> returns it once.
/// </remarks>
internal static class TagTablesDcbQueryFragment
{
    public static void AppendSeqIdPredicate(ICommandBuilder builder, EventGraph events,
        IReadOnlyList<EventTagQueryCondition> conditions, string eventAlias, string tenantId)
    {
        append(events, conditions, eventAlias, tenantId, builder.Append, value => builder.AppendParameter(value));
    }

    public static void AppendSeqIdPredicate(StringBuilder sb, EventGraph events,
        IReadOnlyList<EventTagQueryCondition> conditions, string eventAlias, string tenantId,
        List<object> paramValues)
    {
        append(events, conditions, eventAlias, tenantId, text => sb.Append(text), value =>
        {
            sb.Append("@p");
            sb.Append(paramValues.Count);
            paramValues.Add(value);
        });
    }

    private static void append(EventGraph events, IReadOnlyList<EventTagQueryCondition> conditions,
        string eventAlias, string tenantId, Action<string> text, Action<object> parameter)
    {
        var schema = events.DatabaseSchemaName;

        // Under conjoined tenancy the tag tables carry tenant_id, and under per-tenant event partitioning
        // seq_id is only unique within a tenant (#4645), so each probe is held to the session's tenant.
        var conjoined = events.TenancyStyle == TenancyStyle.Conjoined;

        text(eventAlias);
        text(".seq_id in (");

        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0) text(" union all ");

            var condition = conditions[i];
            var registration = events.FindTagType(condition.TagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{condition.TagType.Name}' is not registered. Call RegisterTagType<{condition.TagType.Name}>() first.");

            text("select t.seq_id from ");
            text(schema);
            text(".mt_event_tag_");
            text(registration.TableSuffix);
            text(" t");

            if (condition.EventType != null)
            {
                // The event type lives on mt_events, so a typed condition joins it inside its own probe.
                text(" join ");
                text(schema);
                text(".mt_events x on x.seq_id = t.seq_id");
                if (conjoined) text(" and x.tenant_id = t.tenant_id");
            }

            text(" where t.value = ");
            parameter(registration.ExtractValue(condition.TagValue)!);

            if (condition.EventType != null)
            {
                text(" and x.type = ");
                parameter(events.EventMappingFor(condition.EventType).EventTypeName);
            }

            if (conjoined)
            {
                text(" and t.tenant_id = ");
                parameter(tenantId);
            }
        }

        text(")");
    }
}
