using System;
using System.Linq;
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
                var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream<Quest>(joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream<Quest>(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(1).ShouldBeOfType<MembersDeparted>();
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream<Quest>(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(1).ShouldBeOfType<MembersDeparted>();
            }
        }

        [Theory]
        [MemberData("SessionTypes")]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back(DocumentTracking sessionType)
        {
            var store = InitStore();

            using (var session = store.OpenSession(sessionType))
            {
                var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined);
                session.Events.AppendEvents(id, departed);

                session.SaveChanges();

                var streamEvents = session.Events.FetchStream<Quest>(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(1).ShouldBeOfType<MembersDeparted>();
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
                var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.AppendEvents(id, joined);
                session.Events.AppendEvents(id, departed);

                session.SaveChanges();

                var streamEvents = session.Events.FetchStream<Quest>(id);

                streamEvents.Count().ShouldBe(3);
                streamEvents.ElementAt(0).ShouldBeOfType<QuestStarted>();
                streamEvents.ElementAt(1).ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(2).ShouldBeOfType<MembersDeparted>();
            }
        }

        private static DocumentStore InitStore()
        {
            var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Connection(ConnectionSource.ConnectionString);

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));
            });

            store.Advanced.Clean.DeleteAllEventData();
            return store;
        }
    }
}