using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class EventGraphTests
    {
        private readonly EventGraph theGraph = new EventGraph();

        [Fact]
        public void find_stream_mapping_initially()
        {
            theGraph.StreamMappingFor<Issue>()
                .DocumentType.ShouldBe(typeof(Issue));
        }

        [Fact]
        public void caches_the_stream_mapping()
        {
            theGraph.StreamMappingFor<Issue>()
                .ShouldBeSameAs(theGraph.StreamMappingFor<Issue>());
        }

        [Fact]
        public void register_event_types_and_retrieve()
        {
            theGraph.StreamMappingFor<Issue>().AddEvent(typeof (IssueAssigned));
            theGraph.StreamMappingFor<Issue>().AddEvent(typeof (IssueCreated));
            theGraph.StreamMappingFor<Quest>().AddEvent(typeof (MembersJoined));
            theGraph.StreamMappingFor<Quest>().AddEvent(typeof (MembersDeparted));

            theGraph.EventMappingFor<IssueAssigned>().ShouldBeTheSameAs(theGraph.EventMappingFor<IssueAssigned>());

            theGraph.EventMappingFor<IssueAssigned>().Stream.DocumentType.ShouldBe(typeof(Issue));

            theGraph.EventMappingFor<MembersJoined>().Stream.DocumentType.ShouldBe(typeof(Quest));
        }


        [Fact]
        public void find_event_by_event_type_name()
        {
            theGraph.StreamMappingFor<Issue>().AddEvent(typeof(IssueAssigned));
            theGraph.StreamMappingFor<Issue>().AddEvent(typeof(IssueCreated));
            theGraph.StreamMappingFor<Quest>().AddEvent(typeof(MembersJoined));
            theGraph.StreamMappingFor<Quest>().AddEvent(typeof(MembersDeparted));

            theGraph.EventMappingFor("members_joined").DocumentType.ShouldBe(typeof(MembersJoined));

            theGraph.EventMappingFor("issue_created").DocumentType.ShouldBe(typeof(IssueCreated));
        }
    }
}