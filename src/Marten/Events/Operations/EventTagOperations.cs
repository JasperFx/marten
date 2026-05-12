using System.Collections.Generic;
using JasperFx.Events;
using Marten.Internal.Sessions;
using Marten.Storage;

namespace Marten.Events.Operations;

internal static class EventTagOperations
{
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
