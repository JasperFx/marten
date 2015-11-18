using System.Linq;
using Marten.Events;
using Marten.Schema;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Events
{
    public class end_to_end_event_capture_and_fetching_the_stream_Tests
    {
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        public void capture_events_to_a_new_stream_and_fetch_the_events_back()
        {
            var schema = _container.GetInstance<IDocumentSchema>();
            var quest = schema.Events.StreamMappingFor<Quest>();

            quest.AddEvent(typeof(MembersJoined));
            quest.AddEvent(typeof(MembersDeparted));


            _container.GetInstance<IDocumentCleaner>().CompletelyRemoveAll();

            _container.GetInstance<ICommandRunner>().Execute(SchemaBuilder.GetText("mt_stream"));

            var events = _container.GetInstance<Marten.Events.Events>();


            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var id = events.StartStream<Quest>(joined, departed);

            var streamEvents = events.FetchStream<Quest>(id);

            streamEvents.Count().ShouldBe(2);
            streamEvents.ElementAt(0).ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(1).ShouldBeOfType<MembersDeparted>();
        }
    }
}