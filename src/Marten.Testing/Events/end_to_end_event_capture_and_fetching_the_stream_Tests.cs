using System.Linq;
using Marten.Schema;
using Marten.Services;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Events
{
    public class end_to_end_event_capture_and_fetching_the_stream_Tests
    {
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back()
        {
            var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = true;

                _.Connection(ConnectionSource.ConnectionString);

                _.Events.AddEventType(typeof (MembersJoined));
                _.Events.AddEventType(typeof (MembersDeparted));
            });

            store.Advanced.Clean.CompletelyRemoveAll();

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
    }
}