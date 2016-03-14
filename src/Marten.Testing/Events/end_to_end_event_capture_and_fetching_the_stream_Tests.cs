using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class end_to_end_event_capture_and_fetching_the_stream_Tests
    {
        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
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

        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
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


        [Fact]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back()
        {
            var store = InitStore();

            using (var session = store.OpenSession())
            {
                var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.AppendEvents(id, joined, departed);
                session.SaveChanges();

                //Throws a   Marten.Testing.Events.end_to_end_event_capture_and_fetching_the_stream_Tests.capture_events_to_a_non_existing_stream_and_fetch_the_events_back [FAIL]
                //Npgsql.NpgsqlException : 23502: null value in column "type" violates not-null constraint
                //>>>>  "type" is stream_type in this case.
                var streamEvents = session.Events.FetchStream<Quest>(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(1).ShouldBeOfType<MembersDeparted>();
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