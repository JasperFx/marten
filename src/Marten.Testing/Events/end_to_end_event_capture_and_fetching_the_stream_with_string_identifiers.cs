using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Events
{
    [Collection("projections")]
    public class end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers : OneOffConfigurationsContext
    {
        public end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers() : base("projections")
        {
        }

        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = "First";

                session.Events.StartStream<Quest>(id, joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
            }
        }

        [Fact]
        public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
            {
                #region sample_start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = "Second";
                session.Events.StartStream<Quest>(id, joined, departed);
                await session.SaveChangesAsync();
                #endregion sample_start-stream-with-aggregate-type

                var streamEvents = await session.Events.FetchStreamAsync(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
            }
        }

        [Fact]
        public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async_with_linq()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
            {
                #region sample_start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = "Third";
                session.Events.StartStream<Quest>(id, joined, departed);
                await session.SaveChangesAsync();
                #endregion sample_start-stream-with-aggregate-type

                var streamEvents = await session.Events.QueryAllRawEvents()
                                                .Where(x => x.StreamKey == id).OrderBy(x => x.Version).ToListAsync();

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
            }
        }

        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_sync_with_linq()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
            {
                #region sample_start-stream-with-aggregate-type
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = "Fourth";
                session.Events.StartStream<Quest>(id, joined, departed);
                session.SaveChanges();
                #endregion sample_start-stream-with-aggregate-type

                var streamEvents = session.Events.QueryAllRawEvents()
                                          .Where(x => x.StreamKey == id).OrderBy(x => x.Version).ToList();

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
            var questId = "Fifth";

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
                var liveAggregate = session.Events.AggregateStream<QuestPartyWithStringIdentifier>(questId);
                var inlinedAggregate = session.Load<QuestPartyWithStringIdentifier>(questId);
                liveAggregate.Id.ShouldBe(inlinedAggregate.Id);
                inlinedAggregate.ToString().ShouldBe(liveAggregate.ToString());
            }
        }

        [Fact]
        public void open_persisted_stream_in_new_store_with_same_settings()
        {
            var store = InitStore("event_store");
            var questId = "Sixth";

            using (var session = store.OpenSession())
            {
                //Note "Id = questId" @see live_aggregate_equals_inlined_aggregate...
                var started = new QuestStarted { Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                session.SaveChanges();
            }

            // events-aggregate-on-the-fly - works with same store
            using (var session = store.OpenSession())
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

            using (var session = store.OpenSession())
            {
                var party = session.Load<QuestPartyWithStringIdentifier>(questId);
                SpecificationExtensions.ShouldNotBeNull(party);
            }

            var newStore = InitStore("event_store", false);

            //Inline is working
            using (var session = store.OpenSession())
            {
                var party = session.Load<QuestPartyWithStringIdentifier>(questId);
                party.ShouldNotBeNull();
            }
            //GetAll
            using (var session = store.OpenSession())
            {
                var parties = session.Events.QueryRawEventDataOnly<QuestPartyWithStringIdentifier>().ToArray();
                foreach (var party in parties)
                {
                    party.ShouldNotBeNull();
                }
            }
            //This AggregateStream fail with NPE
            using (var session = newStore.OpenSession())
            {
                // questId is the id of the stream
                var party = session.Events.AggregateStream<QuestPartyWithStringIdentifier>(questId);//Here we get NPE
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
        public void query_before_saving()
        {
            var store = InitStore("event_store");
            var questId = "Seventh";

            using (var session = store.OpenSession())
            {
                var parties = session.Query<QuestPartyWithStringIdentifier>().ToArray();
                parties.Length.ShouldBeLessThanOrEqualTo(0);
            }

            //This SaveChanges will fail with missing method (ro collection configured?)
            using (var session = store.OpenSession())
            {
                var started = new QuestStarted { Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                session.SaveChanges();

                var party = session.Events.AggregateStream<QuestPartyWithStringIdentifier>(questId);
                SpecificationExtensions.ShouldNotBeNull(party);
            }
        }

        [Fact]
        public async Task aggregate_stream_async_has_the_id()
        {
            var store = InitStore("event_store");
            var questId = "Eighth";

            using (var session = store.OpenSession())
            {
                var parties = await session.Query<QuestPartyWithStringIdentifier>().ToListAsync();
                parties.Count.ShouldBeLessThanOrEqualTo(0);
            }

            //This SaveChanges will fail with missing method (ro collection configured?)
            using (var session = store.OpenSession())
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
            var store = InitStore();

            using (var session = store.OpenSession())
            {
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
        }

        [Fact]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
            {
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
        }

        [Fact]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back()
        {
            var store = InitStore();

            var id = "Twelth";
            var started = new QuestStarted();

            using (var session = store.OpenSession())
            {
                session.Events.StartStream<Quest>(id, started);
                session.SaveChanges();
            }

            using (var session = store.OpenSession())
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
            var store = InitStore("event_store");

            using (var session = store.OpenSession())
            {
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
        }

        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided_in_another_database_schema()
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession())
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = "Fourteen";
                session.Events.StartStream<Quest>(id, joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(x => SpecificationExtensions.ShouldBeGreaterThan(x.Sequence, 0L));
            }
        }

        [Fact]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema()
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession())
            {
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
        }

        [Fact]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back_in_another_database_schema()
        {
            var store = InitStore("event_store");

            var id = "Fifteen";
            var started = new QuestStarted();

            using (var session = store.OpenSession())
            {
                session.Events.StartStream<Quest>(id, started);
                session.SaveChanges();
            }

            using (var session = store.OpenSession())
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
            var store = InitStore("event_store");

            var id = "Sixteen";
            var started = new QuestStarted();

            using (var session = store.OpenSession())
            {
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

        private DocumentStore InitStore(string databascSchema = null, bool cleanSchema = true)
        {
            var store = StoreOptions(_ =>
            {
                if (databascSchema != null)
                {
                    _.Events.DatabaseSchemaName = databascSchema;
                }

                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Connection(ConnectionSource.ConnectionString);

                _.Events.StreamIdentity = StreamIdentity.AsString;
                _.Events.Projections.SelfAggregate<QuestPartyWithStringIdentifier>();

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));
                _.Events.AddEventType(typeof(QuestStarted));
            }, cleanSchema);


            return store;
        }
    }

    public class QuestPartyWithStringIdentifier
    {
        private readonly IList<string> _members = new List<string>();

        public string[] Members
        {
            get
            {
                return _members.ToArray();
            }
            set
            {
                _members.Clear();
                _members.AddRange(value);
            }
        }

        public IList<string> Slayed { get; } = new List<string>();

        public void Apply(MembersJoined joined)
        {
            if (joined.Members != null)
                _members.Fill(joined.Members);
        }

        public void Apply(MembersDeparted departed)
        {
            _members.RemoveAll(x => departed.Members.Contains(x));
        }

        public void Apply(QuestStarted started)
        {
            Name = started.Name;
        }

        public string Key { get; set; }

        public string Name { get; set; }

        public string Id { get; set; }

        public override string ToString()
        {
            return $"Quest party '{Name}' is {Members.Join(", ")}";
        }
    }
}
