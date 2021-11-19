using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Events
{
    [Collection("projections")]
    public class end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests : OneOffConfigurationsContext
    {
        public static TheoryData<DocumentTracking> SessionTypes = new TheoryData<DocumentTracking>
        {
            DocumentTracking.IdentityOnly,
            DocumentTracking.DirtyTracking
        };

        public end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests() : base("projections")
        {
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                #region sample_start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream(joined, departed).Id;
                session.SaveChanges();
                #endregion

                var streamEvents = session.Events.FetchStream(id);

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);

                GenericEnumerableExtensions.Each<IEvent>(streamEvents, e => ShouldBeTestExtensions.ShouldNotBe(e.Timestamp, default(DateTimeOffset)));
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                #region sample_start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream(joined, departed).Id;
                await session.SaveChangesAsync();
                #endregion

                var streamEvents = await session.Events.FetchStreamAsync(id);

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);

                GenericEnumerableExtensions.Each<IEvent>(streamEvents, e => ShouldBeTestExtensions.ShouldNotBe(e.Timestamp, default(DateTimeOffset)));
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async_with_linq(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                #region sample_start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream(joined, departed).Id;
                await session.SaveChangesAsync();
                #endregion

                var streamEvents = await Queryable.Where<IEvent>(session.Events.QueryAllRawEvents(), x => x.StreamId == id).OrderBy(x => x.Version).ToListAsync();

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_sync_with_linq(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                #region sample_start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream(joined, departed).Id;
                session.SaveChanges();
                #endregion

                var streamEvents = Queryable.Where<IEvent>(session.Events.QueryAllRawEvents(), x => x.StreamId == id).OrderBy(x => x.Version).ToList();

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
            }
        }

        [Fact]
        public void live_aggregate_equals_inlined_aggregate_without_hidden_contracts()
        {
            var store = InitStore("event_store");
            var questId = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                //Note Id = questId, is we remove it from first message then AggregateStream will return party.Id=default(Guid) that is not equals to Load<QuestParty> result
                var started = new QuestStarted { /*Id = questId,*/ Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream(questId, started, joined1);
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

        [Fact]
        public void open_persisted_stream_in_new_store_with_same_settings()
        {
            var store = InitStore("event_store");
            var questId = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                //Note "Id = questId" @see live_aggregate_equals_inlined_aggregate...
                var started = new QuestStarted { Id = questId, Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream(questId, started, joined1);
                session.SaveChanges();
            }

            // events-aggregate-on-the-fly - works with same store
            using (var session = store.OpenSession())
            {
                // questId is the id of the stream
                var party = session.Events.AggregateStream<QuestParty>(questId);

                party.Id.ShouldBe(questId);
                SpecificationExtensions.ShouldNotBeNull(party);

                var party_at_version_3 = session.Events
                                                .AggregateStream<QuestParty>(questId, 3);

                SpecificationExtensions.ShouldNotBeNull(party_at_version_3);

                var party_yesterday = session.Events
                                             .AggregateStream<QuestParty>(questId, timestamp: DateTimeOffset.UtcNow.AddDays(-1));
                party_yesterday.ShouldBeNull();
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
                SpecificationExtensions.ShouldNotBeNull(party);
            }
            //GetAll
            using (var session = store.OpenSession())
            {
                var parties = session.Events.QueryRawEventDataOnly<QuestParty>().ToArray<QuestParty>();
                foreach (var party in parties)
                {
                    SpecificationExtensions.ShouldNotBeNull(party);
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
                                             .AggregateStream<QuestParty>(questId, timestamp: DateTimeOffset.UtcNow.AddDays(-1));
                party_yesterday.ShouldBeNull();
            }
        }

        [Fact]
        public void query_before_saving()
        {
            var store = InitStore("event_store");
            var questId = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                var parties = session.Query<QuestParty>().ToArray<QuestParty>();
                parties.Length.ShouldBeLessThanOrEqualTo(0);
            }

            //This SaveChanges will fail with missing method (ro collection configured?)
            using (var session = store.OpenSession())
            {
                var started = new QuestStarted { Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream(questId, started, joined1);
                session.SaveChanges();

                var party = session.Events.AggregateStream<QuestParty>(questId);
                ShouldBeTestExtensions.ShouldBe(party.Id, questId);
            }
        }

        [Fact]
        public async Task aggregate_stream_async_has_the_id()
        {
            var store = InitStore("event_store");
            var questId = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                var parties = await QueryableExtensions.ToListAsync<QuestParty>(session.Query<QuestParty>());
                parties.Count.ShouldBeLessThanOrEqualTo(0);
            }

            //This SaveChanges will fail with missing method (ro collection configured?)
            using (var session = store.OpenSession())
            {
                var started = new QuestStarted { Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream(questId, started, joined1);
                await session.SaveChangesAsync();

                var party = await session.Events.AggregateStreamAsync<QuestParty>(questId);
                party.Id.ShouldBe(questId);
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided(
            DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                #region sample_start-stream-with-existing-guid
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream(id, joined, departed);
                session.SaveChanges();
                #endregion

                var streamEvents = session.Events.FetchStream(id);

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream(id, joined);
                session.Events.Append(id, departed);

                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count<IEvent>().ShouldBe(2);
                streamEvents.ElementAt<IEvent>(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt<IEvent>(0).Version.ShouldBe(1);
                streamEvents.ElementAt<IEvent>(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt<IEvent>(1).Version.ShouldBe(2);
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();

            var id = Guid.NewGuid();
            var started = new QuestStarted();

            using (var session = store.OpenSession(sessionType))
            {
                session.Events.StartStream(id, started);
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

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(3);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<QuestStarted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 2).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 2).Version.ShouldBe(3);
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream(joined, departed).Id;
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
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
                session.Events.StartStream(id, joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);

                GenericEnumerableExtensions.Each<IEvent>(streamEvents, x => SpecificationExtensions.ShouldBeGreaterThan(x.Sequence, 0L));
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream(id, joined);
                session.Events.Append(id, departed);

                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            var id = Guid.NewGuid();
            var started = new QuestStarted();

            using (var session = store.OpenSession(sessionType))
            {
                session.Events.StartStream(id, started);
                session.SaveChanges();
            }

            using (var session = store.OpenSession(sessionType))
            {
                #region sample_append-events
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(id, joined, departed);

                session.SaveChanges();
                #endregion

                var streamEvents = session.Events.FetchStream(id);

                Enumerable.Count<IEvent>(streamEvents).ShouldBe(3);
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<QuestStarted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 0).Version.ShouldBe(1);
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Data.ShouldBeOfType<MembersJoined>();
                Enumerable.ElementAt<IEvent>(streamEvents, 1).Version.ShouldBe(2);
                Enumerable.ElementAt<IEvent>(streamEvents, 2).Data.ShouldBeOfType<MembersDeparted>();
                Enumerable.ElementAt<IEvent>(streamEvents, 2).Version.ShouldBe(3);
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
        public void assert_on_max_event_id_on_event_stream_append(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            var id = Guid.NewGuid();
            var started = new QuestStarted();

            using (var session = store.OpenSession(sessionType))
            {
                #region sample_append-events-assert-on-eventid
                session.Events.StartStream(id, started);
                session.SaveChanges();

                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                // Events are appended into the stream only if the maximum event id for the stream
                // would be 3 after the append operation.
                session.Events.Append(id, 3, joined, departed);

                session.SaveChanges();
                #endregion
            }
        }

        [Theory]
        [MemberData(nameof(SessionTypes))]
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

                ShouldBeTestExtensions.ShouldBe(streamEvents.Count, 1);
                var @event = Enumerable.ElementAt<IEvent>(streamEvents, 0).Data.ShouldBeOfType<ImmutableEvent>();

                @event.Id.ShouldBe(id);
                @event.Name.ShouldBe("some-name");
            }
        }

        private DocumentStore InitStore(string databaseSchema = null, bool cleanSchema = true)
        {
            var store = StoreOptions(_ =>
            {
                if (databaseSchema != null)
                {
                    _.Events.DatabaseSchemaName = databaseSchema;
                }

                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Connection(ConnectionSource.ConnectionString);

                _.Projections.SelfAggregate<QuestParty>();

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));
                _.Events.AddEventType(typeof(QuestStarted));
            }, cleanSchema);


            return store;
        }
    }
}
