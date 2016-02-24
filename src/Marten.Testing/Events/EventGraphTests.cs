using System;
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
            throw new NotImplementedException("REDO");
            /*
            theGraph.StreamMappingFor<Issue>()
                .AggregateType.ShouldBe(typeof(Issue));
                */
        }

        [Fact]
        public void caches_the_stream_mapping()
        {
            throw new NotImplementedException("REDO");
            /*
            theGraph.StreamMappingFor<Issue>()
                .ShouldBeSameAs(theGraph.StreamMappingFor<Issue>());
                */
        }

        [Fact]
        public void register_event_types_and_retrieve()
        {
            throw new NotImplementedException("REDO");

            /*
            theGraph.StreamMappingFor<Issue>().AddEvent(typeof (IssueAssigned));
            theGraph.StreamMappingFor<Issue>().AddEvent(typeof (IssueCreated));
            theGraph.StreamMappingFor<Quest>().AddEvent(typeof (MembersJoined));
            theGraph.StreamMappingFor<Quest>().AddEvent(typeof (MembersDeparted));

            theGraph.EventMappingFor<IssueAssigned>().ShouldBeTheSameAs(theGraph.EventMappingFor<IssueAssigned>());
            */
        }


        [Fact]
        public void find_event_by_event_type_name()
        {
            theGraph.AddEventType(typeof(IssueAssigned));
            theGraph.AddEventType(typeof(IssueCreated));
            theGraph.AddEventType(typeof(MembersJoined));
            theGraph.AddEventType(typeof(MembersDeparted));

            theGraph.EventMappingFor("members_joined").DocumentType.ShouldBe(typeof(MembersJoined));

            theGraph.EventMappingFor("issue_created").DocumentType.ShouldBe(typeof(IssueCreated));
        }
    }
}