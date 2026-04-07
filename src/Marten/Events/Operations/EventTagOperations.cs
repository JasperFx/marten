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

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count == 0) continue;

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

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count == 0) continue;

            foreach (var tag in tags)
            {
                var registration = eventGraph.FindTagType(tag.TagType);
                if (registration == null) continue;

                session.QueueOperation(new InsertEventTagByEventIdOperation(schema, registration, @event.Id, tag.Value, isConjoined, useArchived));
            }
        }
    }
}
