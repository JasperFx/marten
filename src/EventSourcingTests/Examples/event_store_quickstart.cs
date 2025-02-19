using System;
using System.Threading.Tasks;
using Marten;

namespace EventSourcingTests.Examples;

public class event_store_quickstart
{
    #region sample_using-fetch-stream

    public async Task load_event_stream(IDocumentSession session, Guid streamId)
    {
        // Fetch *all* of the events for this stream
        var events1 = await session.Events.FetchStreamAsync(streamId);

        // Fetch the events for this stream up to and including version 5
        var events2 = await session.Events.FetchStreamAsync(streamId, 5);

        // Fetch the events for this stream at this time yesterday
        var events3 = await session.Events
            .FetchStreamAsync(streamId, timestamp: DateTime.UtcNow.AddDays(-1));
    }

    public async Task load_event_stream_async(IDocumentSession session, Guid streamId)
    {
        // Fetch *all* of the events for this stream
        var events1 = await session.Events.FetchStreamAsync(streamId);

        // Fetch the events for this stream up to and including version 5
        var events2 = await session.Events.FetchStreamAsync(streamId, 5);

        // Fetch the events for this stream at this time yesterday
        var events3 = await session.Events
            .FetchStreamAsync(streamId, timestamp: DateTime.UtcNow.AddDays(-1));
    }

    #endregion

    #region sample_load-a-single-event

    public async Task load_a_single_event_synchronously(IDocumentSession session, Guid eventId)
    {
        // If you know what the event type is already
        var event1 = await session.Events.LoadAsync<MembersJoined>(eventId);

        // If you do not know what the event type is
        var event2 = await session.Events.LoadAsync(eventId);
    }

    public async Task load_a_single_event_asynchronously(IDocumentSession session, Guid eventId)
    {
        // If you know what the event type is already
        var event1 = await session.Events.LoadAsync<MembersJoined>(eventId);

        // If you do not know what the event type is
        var event2 = await session.Events.LoadAsync(eventId);
    }

    #endregion

}
