using System;
using Marten.Events;
using Marten.Events.V4Concept;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class EventGraphTests
    {
        private readonly EventGraph theGraph = new EventGraph(new StoreOptions());

        [Fact]
        public void build_event()
        {
            var slayed = new MonsterSlayed {Name = "The Gorgon"};
            var @event = theGraph.BuildEvent(slayed);

            @event.ShouldBeOfType<Event<MonsterSlayed>>();

            @event.Data.ShouldBe(slayed);
            var mapping = theGraph.EventMappingFor<MonsterSlayed>();
            @event.EventTypeName.ShouldBe(mapping.EventTypeName);
            @event.DotNetTypeName.ShouldBe(mapping.DotNetTypeName);
        }

        [Fact]
        public void stream_identity_is_guid_by_default()
        {
            theGraph.StreamIdentity.ShouldBe(StreamIdentity.AsGuid);
        }

        [Fact]
        public void caches_the_stream_mapping()
        {
            theGraph.Projections.AggregatorFor<Issue>()
                .ShouldBeSameAs(theGraph.Projections.AggregatorFor<Issue>());
        }

        [Fact]
        public void register_event_types_and_retrieve()
        {
            theGraph.AddEventType(typeof(IssueAssigned));
            theGraph.AddEventType(typeof(IssueCreated));
            theGraph.AddEventType(typeof(MembersJoined));
            theGraph.AddEventType(typeof(MembersDeparted));

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
        public void has_any_in_starting_state()
        {
            theGraph.IsActive(null).ShouldBeFalse();
        }

        [Fact]
        public void has_any_is_true_with_any_events()
        {
            theGraph.AddEventType(typeof(IssueAssigned));
            theGraph.IsActive(null).ShouldBeTrue();
        }

        public class HouseRemodeling
        {
            public Guid Id { get; set; }
        }
    }
}
