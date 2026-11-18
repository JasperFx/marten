#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Events.Tags;
using Marten.Storage;

namespace Marten.Events.Operations;

/// <summary>
/// #6049: the <c>mt_event_tag_*</c> deletes that every statement deleting events runs first. Each tag table has
/// a foreign key to <c>mt_events</c> that does NOT cascade, so deleting a tagged event while its tag rows remain
/// is refused with 23503. Stream compaction (<see cref="DeleteEventsOperation"/>) and the tombstone
/// (<see cref="DeleteStreamOperation"/>) both build them here, so a change to the tag tables can't reach one and
/// miss the other.
/// </summary>
/// <remarks>
/// Each delete is a data-modifying CTE of the caller's statement. The CTEs share the statement's snapshot, so
/// they still find the events the same statement deletes, and the foreign keys are checked at the end of the
/// statement, when both are gone. The caller says which events by writing a predicate on the tag row's
/// <c>seq_id</c>, and binds the parameters in the order it is asked to write them. That keeps the builder
/// independent of the parameter style, positional <c>?</c> placeholders or <see cref="Weasel.Postgresql.ICommandBuilder"/>
/// parameters.
/// </remarks>
internal static class EventTagRowDeletes
{
    /// <summary>The alias each delete gives its tag table.</summary>
    public const string Alias = "t";

    /// <summary>
    /// The tag tables with rows keyed to <c>mt_events</c>: one per registered tag type, and none under
    /// <see cref="DcbStorageMode.HStore"/>, which keeps the tags on the event rows.
    /// </summary>
    public static IReadOnlyList<ITagTypeRegistration> TablesFor(EventGraph events)
        => events.DcbStorageMode == DcbStorageMode.HStore ? [] : events.TagTypes;

    /// <summary>
    /// Write <c>tag0 as (delete …), tag1 as (delete …)</c>, one CTE per table of <see cref="TablesFor"/>, with no
    /// leading <c>with</c> and no trailing separator. Writes nothing when there is no tag table.
    /// </summary>
    /// <param name="events">The event store's configuration.</param>
    /// <param name="text">Appends SQL text.</param>
    /// <param name="seqIdPredicate">
    /// Appends the predicate that picks the events, given the tag row's <c>seq_id</c> column, e.g.
    /// <c>t.seq_id = ANY($1)</c>.
    /// </param>
    /// <param name="tenantId">
    /// Appends the tenant id parameter. Called under conjoined tenancy only, where the tag tables carry
    /// <c>tenant_id</c> and <c>seq_id</c> alone is not an event (#5234).
    /// </param>
    public static void AppendCtes(EventGraph events, Action<string> text, Action<string> seqIdPredicate,
        Action tenantId)
    {
        var tables = TablesFor(events);
        var conjoined = events.TenancyStyle == TenancyStyle.Conjoined;

        for (var i = 0; i < tables.Count; i++)
        {
            if (i > 0) text(", ");

            text($"tag{i} as (delete from {events.DatabaseSchemaName}.mt_event_tag_{tables[i].TableSuffix} {Alias} where ");
            seqIdPredicate($"{Alias}.seq_id");

            if (conjoined)
            {
                text($" and {Alias}.tenant_id = ");
                tenantId();
            }

            text(")");
        }
    }
}
