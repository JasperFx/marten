using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Testing.Events;
using Marten.Testing.Events.Projections;
using Shouldly;

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
    _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
});

var questId = Guid.NewGuid();

using (var session = store.OpenSession())
{
    var started = new QuestStarted {Name = "Destroy the One Ring"};
    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

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
    // questId is the id of the stream
    var party = session.Events.AggregateStream<QuestParty>(questId);
    Console.WriteLine(party);

    var party_at_version_3 = session.Events
        .AggregateStream<QuestParty>(questId, 3);


    var party_yesterday = session.Events
        .AggregateStream<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
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

            // SAMPLE: load-a-single-event
public void load_a_single_event_synchronously(IDocumentSession session, Guid eventId)
{
    // If you know what the event type is already
    var event1 = session.Events.Load<MembersJoined>(eventId);

    // If you do not know what the event type is
    var event2 = session.Events.Load(eventId);
}

public async Task load_a_single_event_asynchronously(IDocumentSession session, Guid eventId)
{
    // If you know what the event type is already
    var event1 = await session.Events.LoadAsync<MembersJoined>(eventId)
        .ConfigureAwait(false);

    // If you do not know what the event type is
    var event2 = await session.Events.LoadAsync(eventId)
        .ConfigureAwait(false);
}

        // ENDSAMPLE

        // SAMPLE: using_live_transformed_events
        public void using_live_transformed_events(IDocumentSession session)
        {
            var started = new QuestStarted { Name = "Find the Orb" };
            var joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" } };
            var slayed1 = new MonsterSlayed { Name = "Troll" };
            var slayed2 = new MonsterSlayed { Name = "Dragon" };

            MembersJoined joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };


            session.Events.StartStream<Quest>(started, joined, slayed1, slayed2);
            session.SaveChanges();

            // Our MonsterDefeated documents are created inline
            // with the SaveChanges() call above and are available
            // for querying
            session.Query<MonsterDefeated>().Count()
                .ShouldBe(2);
        }
        // ENDSAMPLE
    }
}
