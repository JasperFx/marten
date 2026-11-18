#nullable enable
using System.Collections.Generic;
using System.Text;
using Marten.Events.Schema;

namespace Marten.Events.Dcb;

/// <summary>
/// The <c>mt_dcb_tag_version</c> keys one stream's events reach, as SQL over the events that are still there, for
/// the operations that change what a boundary reads without appending anything: the tombstone (#6035), and
/// archiving and un-archiving (#6078).
/// </summary>
/// <remarks>
/// Every builder writes positional <c>?</c> placeholders and records in <c>parameters</c>, in order, whether each
/// one is the stream identity (<see langword="true" />) or the tenant id (<see langword="false" />).
/// </remarks>
internal static class StreamDcbTagKeys
{
    /// <summary>How many key queries a stream has: one per tag type, or one for the HStore column.</summary>
    public static int PartCount(EventGraph events)
        => events.DcbStorageMode == DcbStorageMode.HStore ? 1 : events.TagTypes.Count;

    /// <summary>
    /// The (tag_table, tag_value) keys of every <c>mt_dcb_tag_version</c> row the stream's events reach: the tag
    /// type's own key, and the <c>{suffix}:{event type}</c> key of the per-event-type versions (#5887).
    /// </summary>
    /// <remarks>
    /// Columns: <c>k</c> the row's tag_table, <c>v</c> the value as text, <c>suffix</c> (HStore) or <c>raw</c>
    /// (tag tables, the value as stored), and <c>type</c> the event type, null on the tag type's own key. The
    /// values are each tag table's <c>value</c> cast to text, which is
    /// <see cref="JasperFx.Events.Tags.TagValueStringifier"/>'s form for the supported tag types (text, uuid,
    /// integer, bigint, smallint), or the hstore pairs, which are written in that form.
    /// </remarks>
    public static void Append(StringBuilder sql, List<bool> parameters, EventGraph events, bool conjoined,
        int part)
    {
        var schema = events.DatabaseSchemaName;

        void streamEvents()
        {
            sql.Append("e.stream_id = ?");
            parameters.Add(true);
            if (conjoined)
            {
                sql.Append(" and e.tenant_id = ?");
                parameters.Add(false);
            }
        }

        if (events.DcbStorageMode == DcbStorageMode.HStore)
        {
            sql.Append("select distinct x.k, x.v, x.suffix, x.type from (select t.key as k, t.value as v, t.key as suffix, null::varchar as type ");
            sql.Append($"from {schema}.mt_events e cross join lateral each(e.tags) t where ");
            streamEvents();
            sql.Append(" union all select t.key || ':' || e.type, t.value, t.key, e.type ");
            sql.Append($"from {schema}.mt_events e cross join lateral each(e.tags) t where ");
            streamEvents();
            sql.Append(") x");
            return;
        }

        var suffix = events.TagTypes[part].TableSuffix;
        var join = conjoined
            ? "e.seq_id = t.seq_id and e.tenant_id = t.tenant_id"
            : "e.seq_id = t.seq_id";

        sql.Append("select distinct x.k, x.v, x.raw, x.type from (");
        sql.Append($"select '{suffix}' as k, t.value::text as v, t.value as raw, null::varchar as type ");
        sql.Append($"from {schema}.mt_event_tag_{suffix} t join {schema}.mt_events e on {join} where ");
        streamEvents();
        sql.Append($" union all select '{suffix}:' || e.type, t.value::text, t.value, e.type ");
        sql.Append($"from {schema}.mt_event_tag_{suffix} t join {schema}.mt_events e on {join} where ");
        streamEvents();
        sql.Append(") x");
    }

    /// <summary>
    /// #6078: one statement giving every tag on the stream's events a new version from the sequence, creating the
    /// row where there is none. For archiving and un-archiving, which leave the events, and so the value, where
    /// they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Creating a missing row is what catches a boundary that captured 0. An un-archive brings back events such a
    /// boundary didn't read, and a save that captured 0 and finds a row with a sequence value loses
    /// (<see cref="DcbTagVersionAssertion"/>). Only the keys the configured <see cref="DcbConflictScope"/> checks
    /// are created; a per-event-type key under <see cref="DcbConflictScope.TagValue"/> is never read.
    /// </para>
    /// <para>
    /// It has to run after the statement that flips the stream, in the same transaction. That statement takes
    /// the stream's row, which a concurrent append holds before any version row, so by the time this starts
    /// its own snapshot such an append has committed and its tags are found. The rows are taken in ordinal
    /// order, the order appends take them in.
    /// </para>
    /// </remarks>
    public static void AppendCreateOrMove(StringBuilder sql, List<bool> parameters, EventGraph events,
        bool conjoined)
    {
        var table = $"{events.DatabaseSchemaName}.{DcbTagVersionTable.TableName}";
        var recordEventTypes = events.DcbConflictScope != DcbConflictScope.TagValue;

        sql.Append($"insert into {table} (tag_table, tag_value, tenant_id, version) select y.k, y.v, ?, ");
        parameters.Add(false);
        sql.Append(DcbTagVersionTable.NextVersionSql(events));
        sql.Append(" from (select distinct z.k, z.v from (");

        var parts = PartCount(events);
        for (var i = 0; i < parts; i++)
        {
            if (i > 0) sql.Append(" union all ");
            sql.Append($"select k{i}.k, k{i}.v from (");
            Append(sql, parameters, events, conjoined, i);
            sql.Append($") k{i}");
            if (!recordEventTypes)
            {
                sql.Append($" where k{i}.type is null");
            }
        }

        sql.Append(") z) y order by y.k collate \"C\", y.v collate \"C\"");
        sql.Append(" on conflict (tag_table, tag_value, tenant_id) do update set version = excluded.version");
    }
}
