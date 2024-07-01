using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.QuickAppend;

[Collection("quick_string_identified_streams")]
public class
    quick_append_event_capture_and_fetching_the_stream_with_string_identifiers: StoreContext<
    StringIdentifiedStreamsFixture>
{
    public quick_append_event_capture_and_fetching_the_stream_with_string_identifiers(
        StringIdentifiedStreamsFixture fixture): base(fixture)
    {
    }

    [Fact]
    public void capture_events_to_a_new_stream_and_fetch_the_events_back()
    {
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "First";

        theSession.Events.StartStream<Quest>(id, joined, departed);
        theSession.SaveChanges();

        var streamEvents = theSession.Events.FetchStream(id);

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);

        streamEvents.Each(e => ShouldBeTestExtensions.ShouldNotBe(e.Timestamp, default(DateTimeOffset)));
    }

    [Fact]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async()
    {
        #region sample_start-stream-with-aggregate-type

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Second";
        theSession.Events.StartStream<Quest>(id, joined, departed);
        await theSession.SaveChangesAsync();

        #endregion

        var streamEvents = await theSession.Events.FetchStreamAsync(id);

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);

        streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
    }

    [Fact]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async_with_linq()
    {
        #region sample_start-stream-with-aggregate-type

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Third";
        theSession.Events.StartStream<Quest>(id, joined, departed);
        await theSession.SaveChangesAsync();

        #endregion

        var streamEvents = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.StreamKey == id).OrderBy(x => x.Version).ToListAsync();

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);

        streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
    }

    [Fact]
    public void capture_events_to_a_new_stream_and_fetch_the_events_back_sync_with_linq()
    {
        #region sample_start-stream-with-aggregate-type

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Fourth";
        theSession.Events.StartStream<Quest>(id, joined, departed);
        theSession.SaveChanges();

        #endregion

        var streamEvents = theSession.Events.QueryAllRawEvents()
            .Where(x => x.StreamKey == id).OrderBy(x => x.Version).ToList();

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);

        streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
    }

    [Fact]
    public void live_aggregate_equals_inlined_aggregate_without_hidden_contracts()
    {
        var questId = "Fifth";

        using (var session = theStore.LightweightSession())
        {
            //Note Id = questId, is we remove it from first message then AggregateStream will return party.Id=default(Guid) that is not equals to Load<QuestParty> result
            var started = new QuestStarted
            {
                /*Id = questId,*/
                Name = "Destroy the One Ring"
            };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            var liveAggregate = session.Events.AggregateStream<QuestPartyWithStringIdentifier>(questId);
            var inlinedAggregate = session.Load<QuestPartyWithStringIdentifier>(questId);
            liveAggregate.Id.ShouldBe(inlinedAggregate.Id);
            inlinedAggregate.ToString().ShouldBe(liveAggregate.ToString());
        }
    }

    [Fact]
    public void open_persisted_stream_in_new_store_with_same_settings()
    {
        var questId = "Sixth";

        using (var session = theStore.LightweightSession())
        {
            //Note "Id = questId" @see live_aggregate_equals_inlined_aggregate...
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            session.SaveChanges();
        }

        // events-aggregate-on-the-fly - works with same store
        using (var session = theStore.LightweightSession())
        {
            // questId is the id of the stream
            var party = session.Events.AggregateStream<QuestPartyWithStringIdentifier>(questId);

            party.ShouldNotBeNull();

            var party_at_version_3 = session.Events
                .AggregateStream<QuestPartyWithStringIdentifier>(questId, 3);

            party_at_version_3.ShouldNotBeNull();

            var party_yesterday = session.Events
                .AggregateStream<QuestPartyWithStringIdentifier>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
            party_yesterday.ShouldBeNull();
        }

        using (var session = theStore.LightweightSession())
        {
            var party = session.Load<QuestPartyWithStringIdentifier>(questId);
            party.ShouldNotBeNull();
        }

        var newStore = new DocumentStore(theStore.Options);

        //Inline is working
        using (var session = newStore.LightweightSession())
        {
            var party = session.Load<QuestPartyWithStringIdentifier>(questId);
            party.ShouldNotBeNull();
        }

        //GetAll
        using (var session = theStore.LightweightSession())
        {
            var parties = session.Events.QueryRawEventDataOnly<QuestPartyWithStringIdentifier>().ToArray();
            foreach (var party in parties)
            {
                party.ShouldNotBeNull();
            }
        }

        //This AggregateStream fail with NPE
        using (var session = newStore.LightweightSession())
        {
            // questId is the id of the stream
            var party = session.Events.AggregateStream<QuestPartyWithStringIdentifier>(questId); //Here we get NPE
            party.ShouldNotBeNull();

            var party_at_version_3 = session.Events
                .AggregateStream<QuestPartyWithStringIdentifier>(questId, 3);
            party_at_version_3.ShouldNotBeNull();

            var party_yesterday = session.Events
                .AggregateStream<QuestPartyWithStringIdentifier>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
            party_yesterday.ShouldBeNull();
        }
    }

    [Fact]
    public async Task query_before_saving()
    {
        var questId = "Seventh";

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(QuestPartyWithStringIdentifier));

        await using (var session = theStore.LightweightSession())
        {
            var parties = await session.Query<QuestPartyWithStringIdentifier>().CountAsync();
            parties.ShouldBeLessThanOrEqualTo(0);
        }

        //This SaveChanges will fail with missing method (ro collection configured?)
        await using (var session = theStore.LightweightSession())
        {
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            await session.SaveChangesAsync();

            var party = await session.Events.AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId);
            SpecificationExtensions.ShouldNotBeNull(party);
        }
    }

    [Fact]
    public async Task aggregate_stream_async_has_the_id()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(QuestPartyWithStringIdentifier));

        var questId = "Eighth";

        await using (var session = theStore.LightweightSession())
        {
            var parties = await session.Query<QuestPartyWithStringIdentifier>().ToListAsync();
            parties.Count.ShouldBeLessThanOrEqualTo(0);
        }

        //This SaveChanges will fail with missing method (ro collection configured?)
        await using (var session = theStore.LightweightSession())
        {
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            await session.SaveChangesAsync();

            var party = await session.Events.AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId);
            SpecificationExtensions.ShouldNotBeNull(party);
        }
    }

    [Fact]
    public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided()
    {
        using var session = theStore.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Tenth";
        session.Events.StartStream<Quest>(id, joined, departed);
        session.SaveChanges();

        var streamEvents = session.Events.FetchStream(id);

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }

    [Fact]
    public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back()
    {
        using var session = theStore.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Eleventh";
        session.Events.StartStream<Quest>(id, joined);
        session.Events.Append(id, departed);

        session.SaveChanges();

        var streamEvents = session.Events.FetchStream(id);

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }

    [Fact]
    public void capture_events_to_an_existing_stream_and_fetch_the_events_back()
    {
        var id = "Twelth";
        var started = new QuestStarted();

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Quest>(id, started);
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(id, joined);
            session.Events.Append(id, departed);

            session.SaveChanges();

            var streamEvents = session.Events.FetchStream(id);

            streamEvents.Count().ShouldBe(3);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);
            streamEvents.ElementAt(2).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(2).Version.ShouldBe(3);
        }
    }

    [Fact]
    public void capture_events_to_a_new_stream_and_fetch_the_events_back_in_another_database_schema()
    {
        using var session = theStore.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Thirteen";
        session.Events.StartStream<Quest>(id, joined, departed);
        session.SaveChanges();

        var streamEvents = session.Events.FetchStream(id);

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }


    [Fact]
    public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema()
    {
        using var session = theStore.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Fourteen";
        session.Events.StartStream<Quest>(id, joined);
        session.Events.Append(id, departed);

        session.SaveChanges();

        var streamEvents = session.Events.FetchStream(id);

        streamEvents.Count().ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }

    [Fact]
    public void capture_events_to_an_existing_stream_and_fetch_the_events_back_in_another_database_schema()
    {
        var id = "Fifteen";
        var started = new QuestStarted();

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Quest>(id, started);
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(id, joined, departed);

            session.SaveChanges();

            var streamEvents = session.Events.FetchStream(id);

            streamEvents.Count().ShouldBe(3);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);
            streamEvents.ElementAt(2).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(2).Version.ShouldBe(3);
        }
    }

    [Fact]
    public void assert_on_max_event_id_on_event_stream_append()
    {
        var id = "Sixteen";
        var started = new QuestStarted();

        using var session = theStore.LightweightSession();
        session.Events.StartStream<Quest>(id, started);
        session.SaveChanges();

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        // Events are appended into the stream only if the maximum event id for the stream
        // would be 3 after the append operation.
        session.Events.Append(id, 3, joined, departed);

        session.SaveChanges();
    }
}
