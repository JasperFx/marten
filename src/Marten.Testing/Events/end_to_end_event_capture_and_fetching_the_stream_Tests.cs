using System;
using System.Linq;
using Baseline;
using Marten.Testing.Events.Projections;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class end_to_end_event_capture_and_fetching_the_stream_Tests
    {
        public static TheoryData<DocumentTracking> SessionTypes = new TheoryData<DocumentTracking>
        {
            DocumentTracking.IdentityOnly,
            DocumentTracking.DirtyTracking
        };


        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();


            using (var session = store.OpenSession(sessionType))
            {
                // SAMPLE: start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream<Quest>(joined, departed);
                session.SaveChanges();
                // ENDSAMPLE

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }
        }


        [Theory]
        [MemberData("SessionTypes")]
        public void live_aggregate_equals_inlined_aggregate_without_hidden_contracts(DocumentTracking sessionType)
        {
            var store = InitStore("event_store");
            var questId = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                //Note Id = questId, is we remove it from first message then AggregateStream will return party.Id=default(Guid) that is not equals to Load<QuestParty> result
                var started = new QuestStarted { /*Id = questId,*/ Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                session.SaveChanges();
            }

            using (var session = store.OpenSession())
            {
                var liveAggregate = session.Events.AggregateStream<QuestParty>(questId);
                var inlinedAggregate = session.Load<QuestParty>(questId);
                liveAggregate.Id.ShouldBe(inlinedAggregate.Id);
                inlinedAggregate.ToString().ShouldBe(liveAggregate.ToString());
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void open_persisted_stream_in_new_store_with_same_settings(DocumentTracking sessionType)
        {
            var store = InitStore("event_store");
            var questId = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                //Note "Id = questId" @see live_aggregate_equals_inlined_aggregate...
                var started = new QuestStarted { Id = questId, Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                session.SaveChanges();
            }

            // events-aggregate-on-the-fly - works with same store
            using (var session = store.OpenSession())
            {
                // questId is the id of the stream
                var party = session.Events.AggregateStream<QuestParty>(questId);

                party.Id.ShouldBe(questId);
                party.ShouldNotBeNull();

                var party_at_version_3 = session.Events
                    .AggregateStream<QuestParty>(questId, 3);

                party_at_version_3.ShouldNotBeNull();

                var party_yesterday = session.Events
                    .AggregateStream<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
                party_yesterday.ShouldNotBeNull();
            }

            using (var session = store.OpenSession())
            {
                var party = session.Load<QuestParty>(questId);
                party.Id.ShouldBe(questId);
            }

            var newStore = InitStore("event_store", false);

            //Inline is working
            using (var session = store.OpenSession())
            {
                var party = session.Load<QuestParty>(questId);
                party.ShouldNotBeNull();
            }
            //GetAll
            using (var session = store.OpenSession())
            {
                var parties = session.Events.QueryRawEventDataOnly<QuestParty>().ToArray();
                foreach (var party in parties)
                {
                    party.ShouldNotBeNull();
                }
            }
            //This AggregateStream fail with NPE
            using (var session = newStore.OpenSession())
            {
                // questId is the id of the stream
                var party = session.Events.AggregateStream<QuestParty>(questId);//Here we get NPE
                party.Id.ShouldBe(questId);

                var party_at_version_3 = session.Events
                    .AggregateStream<QuestParty>(questId, 3);
                party_at_version_3.Id.ShouldBe(questId);

                var party_yesterday = session.Events
                    .AggregateStream<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
                party_yesterday.Id.ShouldBe(questId);
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void query_before_saving(DocumentTracking sessionType)
        {
            var store = InitStore("event_store");
            var questId = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                var parties = session.Query<QuestParty>().ToArray();
                parties.Length.ShouldBeLessThanOrEqualTo(0);
            }

            //This SaveChanges will fail with missing method (ro collection configured?)
            using (var session = store.OpenSession())
            {
                var started = new QuestStarted { Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                session.SaveChanges();

                var party = session.Events.AggregateStream<QuestParty>(questId);
                party.Id.ShouldBe(questId);
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided(
            DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                // SAMPLE: start-stream-with-existing-guid
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined, departed);
                session.SaveChanges();
                // ENDSAMPLE

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
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
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();

            var id = Guid.NewGuid();
            var started = new QuestStarted();

            using (var session = store.OpenSession(sessionType))
            {
                session.Events.StartStream<Quest>(id, started);
                session.SaveChanges();
            }

            using (var session = store.OpenSession(sessionType))
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

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream<Quest>(joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void
            capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(x => x.Sequence.ShouldBeGreaterThan(0L));
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
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
        }


        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            var id = Guid.NewGuid();
            var started = new QuestStarted();

            using (var session = store.OpenSession(sessionType))
            {
                session.Events.StartStream<Quest>(id, started);
                session.SaveChanges();
            }

            using (var session = store.OpenSession(sessionType))
            {
                // SAMPLE: append-events
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(id, joined, departed);

                session.SaveChanges();
                // ENDSAMPLE

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

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_immutable_events(DocumentTracking sessionType)
        {
            var store = InitStore();

            var id = Guid.NewGuid();
            var immutableEvent = new ImmutableEvent(id, "some-name");

            using (var session = store.OpenSession(sessionType))
            {
                session.Events.Append(id, immutableEvent);
                session.SaveChanges();
            }

            using (var session = store.OpenSession(sessionType))
            {
                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count.ShouldBe(1);
                var @event = streamEvents.ElementAt(0).Data.ShouldBeOfType<ImmutableEvent>();

                @event.Id.ShouldBe(id);
                @event.Name.ShouldBe("some-name");
            }
        }


        private static DocumentStore InitStore(string databascSchema = null, bool cleanShema = true)
        {
            var store = DocumentStore.For(_ =>
            {
                if (databascSchema != null)
                {
                    _.Events.DatabaseSchemaName = databascSchema;
                }

                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Connection(ConnectionSource.ConnectionString);

                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));
                _.Events.AddEventType(typeof(QuestStarted));
            });

            if (cleanShema)
            {
                store.Advanced.Clean.CompletelyRemoveAll();
            }

            return store;
        }
    }
}