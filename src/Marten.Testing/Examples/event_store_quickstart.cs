using System;
using System.Threading.Tasks;
using Baseline;
using Marten.Testing.Events;
using Marten.Testing.Events.Projections;

namespace Marten.Testing.Examples
{
    public class event_store_quickstart
    {
        public void capture_events()
        {
// SAMPLE: event-store-quickstart            
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.Events.AggregateStreamsInlineWith<QuestParty>();
});

var questId = Guid.NewGuid();

using (var session = store.OpenSession())
{
    var started = new QuestStarted {Name = "Destroy the One Ring"};
    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

    // Start a brand new stream and commit the new events as 
    // part of a transaction
    session.Events.StartStream<Quest>(questId, started, joined1);
    session.SaveChanges();

    // Append more events to the same stream
    var joined2 = new MembersJoined(3, "Buckland", "Merry", "Pippen");
    var joined3 = new MembersJoined(10, "Bree", "Aragorn");
    var arrived = new ArrivedAtLocation { Day = 15, Location = "Rivendell" };
    session.Events.Append(questId, joined2, joined3, arrived);
    session.SaveChanges();
}
// ENDSAMPLE

// SAMPLE: events-fetching-stream
using (var session = store.OpenSession())
{
    var events = session.Events.FetchStream(questId);
    events.Each(evt =>
    {
        Console.WriteLine($"{evt.Version}.) {evt.Data}");
    });
}
// ENDSAMPLE

// SAMPLE: events-aggregate-on-the-fly
using (var session = store.OpenSession())
{
    var party = session.Events.AggregateStream<QuestParty>(questId);
    Console.WriteLine(party);
}
// ENDSAMPLE


using (var session = store.OpenSession())
{
    var party = session.Load<QuestParty>(questId);
    Console.WriteLine(party);
}


        }

// SAMPLE: using-fetch-stream
        public void load_event_stream(IDocumentSession session, Guid streamId)
        {
            // Fetch *all* of the events for this stream
            var events1 = session.Events.FetchStream(streamId);

            // Fetch the events for this stream up to and including version 5
            var events2 = session.Events.FetchStream(streamId, 5);

            // Fetch the events for this stream at this time yesterday
            var events3 = session.Events
                        .FetchStream(streamId, timestamp: DateTime.UtcNow.AddDays(-1));
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
// ENDSAMPLE

    }
}