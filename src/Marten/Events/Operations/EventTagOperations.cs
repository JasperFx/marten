using System.Collections.Generic;
using JasperFx.Events;
using Marten.Events.Dcb;
using Marten.Internal.Sessions;
using Marten.Storage;

namespace Marten.Events.Operations;

internal static class EventTagOperations
{
    /// <summary>
    /// #4591: queue the producer-side bump of <c>mt_dcb_tag_version</c> for every
    /// distinct (tag_table, tag_value) tuple appearing on the stream's tagged
    /// events. Must be called for every save that may write tagged events,
    /// regardless of whether tags are persisted via per-type tables (TagTables),
    /// the HStore column, or the bulk PostgreSQL function — without this, plain
    /// <c>session.Events.Append</c> commits silently bypass the DCB boundary
    /// check held by another in-flight session.
    /// </summary>
    public static void QueueDcbVersionBumpIfNeeded(EventGraph eventGraph, DocumentSessionBase session, StreamAction stream)
    {
        if (eventGraph.TagTypes.Count == 0) return;

        var seen = new HashSet<(string, string)>();
        var entries = new List<(string TagTable, string TagValue)>();

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count == 0) continue;

            CollectDcbVersionTargets(eventGraph, tags, seen, entries);
        }

        if (entries.Count > 0)
        {
            session.QueueOperation(new DcbTagVersionBumpOperation(eventGraph, entries));
        }
    }

    /// <summary>
    /// Queue tag inserts using pre-assigned sequence numbers (Rich append mode).
    /// </summary>
    public static void QueueTagOperations(EventGraph eventGraph, DocumentSessionBase session, StreamAction stream)
    {
        if (eventGraph.TagTypes.Count == 0) return;

        var schema = eventGraph.DatabaseSchemaName;
        var isConjoined = eventGraph.TenancyStyle == TenancyStyle.Conjoined;
        var useArchived = eventGraph.UseArchivedStreamPartitioning;
        var isHStore = eventGraph.DcbStorageMode == DcbStorageMode.HStore;

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count == 0) continue;

            if (isHStore)
            {
                var hstore = BuildHstore(eventGraph, tags);
                if (hstore.Count == 0) continue;

                session.QueueOperation(new SetEventTagsHstoreOperation(schema, @event.Sequence, hstore, isConjoined));
                continue;
            }

            foreach (var tag in tags)
            {
                var registration = eventGraph.FindTagType(tag.TagType);
                if (registration == null) continue;

                session.QueueOperation(new InsertEventTagOperation(schema, registration, @event.Sequence, tag.Value, isConjoined, useArchived));
            }
        }
    }

    /// <summary>
    /// Queue tag inserts using event id lookup (Quick append mode where sequences aren't pre-assigned).
    /// </summary>
    public static void QueueTagOperationsByEventId(EventGraph eventGraph, DocumentSessionBase session, StreamAction stream)
    {
        if (eventGraph.TagTypes.Count == 0) return;

        var schema = eventGraph.DatabaseSchemaName;
        var isConjoined = eventGraph.TenancyStyle == TenancyStyle.Conjoined;
        var useArchived = eventGraph.UseArchivedStreamPartitioning;
        var isHStore = eventGraph.DcbStorageMode == DcbStorageMode.HStore;

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count == 0) continue;

            if (isHStore)
            {
                var hstore = BuildHstore(eventGraph, tags);
                if (hstore.Count == 0) continue;

                session.QueueOperation(new SetEventTagsHstoreByEventIdOperation(schema, @event.Id, hstore, isConjoined));
                continue;
            }

            foreach (var tag in tags)
            {
                var registration = eventGraph.FindTagType(tag.TagType);
                if (registration == null) continue;

                session.QueueOperation(new InsertEventTagByEventIdOperation(schema, registration, @event.Id, tag.Value, isConjoined, useArchived));
            }
        }
    }

    // #4591: collect canonical (tag_table, tag_value) tuples for the
    // mt_dcb_tag_version producer-bump operation. Skips tag types that aren't
    // registered for storage and dedupes tuples already seen in this save.
    private static void CollectDcbVersionTargets(EventGraph eventGraph,
        IReadOnlyList<EventTag> tags,
        HashSet<(string, string)> seen,
        List<(string TagTable, string TagValue)> entries)
    {
        foreach (var tag in tags)
        {
            var registration = eventGraph.FindTagType(tag.TagType);
            if (registration == null) continue;

            var raw = registration.ExtractValue(tag.Value);
            if (raw == null) continue;

            var canonical = TagValueStringifier.Stringify(raw);
            var key = (registration.TableSuffix, canonical);
            if (seen.Add(key))
            {
                entries.Add((registration.TableSuffix, canonical));
            }
        }
    }

    /// <summary>
    /// Build an HSTORE-compatible <c>Dictionary&lt;string, string&gt;</c> from an event's
    /// tag bag. Tags whose type isn't registered are skipped (mirrors the per-tag-table
    /// path). Key is the registered tag's <c>TableSuffix</c>, value is the stringified
    /// tag value (Npgsql maps the dictionary to hstore via <c>NpgsqlDbType.Hstore</c>).
    /// </summary>
    private static Dictionary<string, string> BuildHstore(EventGraph eventGraph,
        IReadOnlyList<EventTag> tags)
    {
        var result = new Dictionary<string, string>(capacity: tags.Count);
        foreach (var tag in tags)
        {
            var registration = eventGraph.FindTagType(tag.TagType);
            if (registration == null) continue;

            var rawValue = registration.ExtractValue(tag.Value);
            if (rawValue == null) continue;

            // hstore values are always text — Npgsql will coerce the Dictionary<string,string>
            // to hstore via NpgsqlDbType.Hstore, so we stringify primitives here.
            result[registration.TableSuffix] = rawValue.ToString()!;
        }

        return result;
    }
}
