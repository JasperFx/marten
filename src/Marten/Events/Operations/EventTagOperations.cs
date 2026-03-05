using JasperFx.Events;
using Marten.Internal.Sessions;

namespace Marten.Events.Operations;

internal static class EventTagOperations
{
    public static void QueueTagOperations(EventGraph eventGraph, DocumentSessionBase session, StreamAction stream)
    {
        if (eventGraph.TagTypes.Count == 0) return;

        var schema = eventGraph.DatabaseSchemaName;

        foreach (var @event in stream.Events)
        {
            var tags = @event.Tags;
            if (tags == null || tags.Count == 0) continue;

            foreach (var tag in tags)
            {
                var registration = eventGraph.FindTagType(tag.TagType);
                if (registration == null) continue;

                // Tag values are already extracted by the WithTag extension method
                session.QueueOperation(new InsertEventTagOperation(schema, registration, @event.Sequence, tag.Value, valueAlreadyExtracted: true));
            }
        }
    }
}
