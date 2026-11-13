using JasperFx.Events.Tags;
using System;
using System.Collections.Generic;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events.Dcb;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Storage;

namespace Marten.Events.Operations;

internal static class EventTagOperations
{
    /// <summary>
    /// #4591: collect the producer-side bump of <c>mt_dcb_tag_version</c> for every
    /// distinct (tag_table, tag_value) tuple appearing on the stream's tagged
    /// events. Must be called for every save that may write tagged events,
    /// regardless of whether tags are persisted via per-type tables (TagTables),
    /// the HStore column, or the bulk PostgreSQL function — without this, plain
    /// <c>session.Events.Append</c> commits silently bypass the DCB boundary
    /// check held by another in-flight session.
    ///
    /// #5887: this is also the ONLY thing that moves a version. The boundary check locks and
    /// compares the rows it read without bumping them, so a boundary conflicts with a later save
    /// exactly when that save appended an event carrying a tag the boundary read. The bumps are
    /// collected on the session rather than queued per stream, and queued once together with the
    /// boundary check -- see <see cref="DocumentSessionBase.QueueDcbTagVersionOperations"/>.
    /// </summary>
    public static void CollectDcbVersionBumps(EventGraph eventGraph, DocumentSessionBase session, StreamAction stream)
    {
        if (eventGraph.TagTypes.Count == 0) return;

        // #5887: anything but the default also maintains a version per (tag value, event type), the
        // one a query condition naming event types checks under DcbConflictScope.TagValueAndEventType.
        var recordEventTypes = eventGraph.DcbConflictScope != DcbConflictScope.TagValue;

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count == 0) continue;

            foreach (var tag in tags)
            {
                var registration = eventGraph.FindTagType(tag.TagType);
                if (registration == null) continue;

                var raw = registration.ExtractValue(tag.Value);
                if (raw == null) continue;

                var tagValue = TagValueStringifier.Stringify(raw);
                session.CollectDcbVersionBump(registration.TableSuffix, tagValue);

                if (recordEventTypes)
                {
                    session.CollectDcbVersionBump(
                        DcbTagVersionKeys.ForEventType(registration.TableSuffix, @event.EventTypeName), tagValue);
                }
            }
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

    /// <summary>
    /// #5265: queue the tag inserts the bulk <c>mt_quick_append_events</c> function cannot write.
    /// Its signature carries one <c>varchar[]</c> per registered tag type, parallel to the events
    /// array, so it has exactly one slot per (event, tag type) and
    /// <see cref="QuickAppendEventsOperationBase.writeAllTagValues" /> fills that slot from the
    /// FIRST matching tag. An event legitimately carrying two tags of one type — "homework copied",
    /// naming the student copied from and the student who copied — would silently lose the rest,
    /// and a lost tag is not an error to a DCB query, it is simply absent from the answer.
    ///
    /// So the function keeps writing the first of each type and this writes the surplus. Skipping
    /// the first here is what keeps the two halves from fighting; the tag tables' primary key is
    /// (value, [tenant_id], seq_id[, is_archived]), so distinct values on one event are distinct
    /// rows and the operation's <c>on conflict do nothing</c> never fires for them.
    /// </summary>
    public static void QueueSurplusTagOperationsByEventId(EventGraph eventGraph, DocumentSessionBase session,
        StreamAction stream)
    {
        if (eventGraph.TagTypes.Count == 0) return;

        var schema = eventGraph.DatabaseSchemaName;
        var isConjoined = eventGraph.TenancyStyle == TenancyStyle.Conjoined;
        var useArchived = eventGraph.UseArchivedStreamPartitioning;

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count < 2) continue;

            var seen = new HashSet<Type>();
            foreach (var tag in tags)
            {
                var registration = eventGraph.FindTagType(tag.TagType);
                if (registration == null) continue;

                // The first tag of each registered type is the one the function writes.
                if (seen.Add(registration.TagType)) continue;

                session.QueueOperation(new InsertEventTagByEventIdOperation(schema, registration, @event.Id,
                    tag.Value, isConjoined, useArchived));
            }
        }
    }

    /// <summary>
    /// Build an HSTORE-compatible <c>Dictionary&lt;string, string&gt;</c> from an event's
    /// tag bag. Tags whose type isn't registered are skipped (mirrors the per-tag-table
    /// path). Key is the registered tag's <c>TableSuffix</c>, value is the stringified
    /// tag value (Npgsql maps the dictionary to hstore via <c>NpgsqlDbType.Hstore</c>).
    /// </summary>
    /// <summary>
    /// Internal rather than private because the bulk import writes the same value into the same column,
    /// and a second implementation of this rule would be a second thing to keep in step.
    /// </summary>
    internal static Dictionary<string, string> BuildHstore(EventGraph eventGraph,
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
            var value = rawValue.ToString()!;

            // #5265: hstore maps one key to one value, and the key here is the tag type. A second
            // tag of the same type has nowhere to go, so this storage mode cannot represent it at
            // all -- unlike TagTables, where the surplus is written as extra rows. Fail loudly
            // rather than dropping it: an absent tag is invisible to a DCB query rather than being
            // an error, which is exactly the failure a consistency boundary must not have.
            if (result.TryGetValue(registration.TableSuffix, out var existing) && existing != value)
            {
                throw new MartenNotSupportedException(
                    $"An event carries more than one '{registration.TagType.NameInCode()}' tag ('{existing}' and '{value}'). DcbStorageMode.HStore cannot store that — its hstore column holds one value per tag type. Use DcbStorageMode.TagTables for events tagged with several values of one type.");
            }

            result[registration.TableSuffix] = value;
        }

        return result;
    }
}
