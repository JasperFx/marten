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
            theGraph.AggregateFor<Issue>()
                .AggregateType.ShouldBe(typeof(Issue));
                
        }

        [Fact]
        public void caches_the_stream_mapping()
        {
            theGraph.AggregateFor<Issue>()
                .ShouldBeSameAs(theGraph.AggregateFor<Issue>());
        }

        [Fact]
        public void register_event_types_and_retrieve()
        {
            theGraph.AddEventType(typeof (IssueAssigned));
            theGraph.AddEventType(typeof (IssueCreated));
            theGraph.AddEventType(typeof (MembersJoined));
            theGraph.AddEventType(typeof (MembersDeparted));

            theGraph.EventMappingFor<IssueAssigned>().ShouldBeTheSameAs(theGraph.EventMappingFor<IssueAssigned>());
            
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

        [Fact]
        public void derives_the_stream_type_name()
        {

            theGraph.AggregateFor<HouseRemodeling>().Alias.ShouldBe("house_remodeling");
            theGraph.AggregateFor<Quest>().Alias.ShouldBe("quest");
        }

        public class HouseRemodeling : IAggregate
        {
            public Guid Id { get; set; }
        }
    }
}