using System;
using System.Linq;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events
{
    public class end_to_end_event_capture_and_fetching_the_stream_Tests
    {
        public static TheoryData<DocumentTracking> SessionTypes = new TheoryData<DocumentTracking>
        {
            DocumentTracking.IdentityOnly,
            DocumentTracking.DirtyTracking
        };

        private readonly ITestOutputHelper _output;

        public end_to_end_event_capture_and_fetching_the_stream_Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();


            using (var session = store.OpenSession(sessionType))
            {
                // SAMPLE: start-stream-with-aggregate-type
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

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
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided(
            DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                // SAMPLE: start-stream-with-existing-guid
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

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
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

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
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

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
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

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
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

                var id = Guid.NewGuid();
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

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType)
        {
            var store = InitStore("event_store");

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

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
                var joined = new MembersJoined {Members = new[] {"Rand", "Matt", "Perrin", "Thom"}};
                var departed = new MembersDeparted {Members = new[] {"Thom"}};

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


        private static DocumentStore InitStore(string databascSchema = null)
        {
            var store = DocumentStore.For(_ =>
            {
                if (databascSchema != null)
                {
                    _.Events.DatabaseSchemaName = databascSchema;
                }

                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Connection(ConnectionSource.ConnectionString);

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));
            });

            store.Advanced.Clean.CompletelyRemoveAll();

            return store;
        }
    }
}