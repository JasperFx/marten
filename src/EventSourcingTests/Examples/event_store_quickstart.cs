using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using EventSourcingTests.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace EventSourcingTests.Examples;

public class event_store_quickstart
{
    public async Task capture_events()
    {
        #region sample_event-store-quickstart
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.Projections.SelfAggregate<QuestParty>();
        });

        var questId = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

            // Start a brand new stream and commit the new events as
            // part of a transaction
            session.Events.StartStream<Quest>(questId, started, joined1);

            // Append more events to the same stream
            var joined2 = new MembersJoined(3, "Buckland", "Merry", "Pippen");
            var joined3 = new MembersJoined(10, "Bree", "Aragorn");
            var arrived = new ArrivedAtLocation { Day = 15, Location = "Rivendell" };
            session.Events.Append(questId, joined2, joined3, arrived);

            // Save the pending changes to db
            await session.SaveChangesAsync();
        }
        #endregion


        #region sample_event-store-start-stream-with-explicit-type

        await using (var session = store.LightweightSession())
        {
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

            // Start a brand new stream and commit the new events as
            // part of a transaction
            session.Events.StartStream(typeof(Quest), questId, started, joined1);
            await session.SaveChangesAsync();
        }
        #endregion

        #region sample_event-store-start-stream-with-no-type

        await using (var session = store.LightweightSession())
        {
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

            // Start a brand new stream and commit the new events as
            // part of a transaction
            // no stream type will be stored in database
            session.Events.StartStream(questId, started, joined1);
            await session.SaveChangesAsync();
        }
        #endregion

        #region sample_events-fetching-stream

        await using (var session = store.LightweightSession())
        {
            var events = await session.Events.FetchStreamAsync(questId);
            events.Each(evt =>
            {
                Console.WriteLine($"{evt.Version}.) {evt.Data}");
            });
        }
        #endregion

        #region sample_events-aggregate-on-the-fly

        await using (var session = store.LightweightSession())
        {
            // questId is the id of the stream
            var party = session.Events.AggregateStream<QuestParty>(questId);
            Console.WriteLine(party);

            var party_at_version_3 = await session.Events
                .AggregateStreamAsync<QuestParty>(questId, 3);

            var party_yesterday = await session.Events
                .AggregateStreamAsync<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
        }
        #endregion

        await using (var session = store.LightweightSession())
        {
            var party = session.Load<QuestParty>(questId);
            Console.WriteLine(party);
        }
    }

    #region sample_using-fetch-stream
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

    #endregion

    #region sample_load-a-single-event
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
        var event1 = await session.Events.LoadAsync<MembersJoined>(eventId);

        // If you do not know what the event type is
        var event2 = await session.Events.LoadAsync(eventId);
    }

    #endregion

    #region sample_using_live_transformed_events
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

    #endregion
}
