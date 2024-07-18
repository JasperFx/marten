using System;
using EventSourcingTests.Projections;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

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
    public void enable_unique_index_on_event_id_is_false_by_default()
    {
        theGraph.EnableUniqueIndexOnEventId.ShouldBeFalse();
    }

    [Fact]
    public void stream_identity_is_guid_by_default()
    {
        theGraph.StreamIdentity.ShouldBe(StreamIdentity.AsGuid);
    }

    [Fact]
    public void caches_the_stream_mapping()
    {
        theGraph.Options.Projections.AggregatorFor<IssueAggregate>()
            .ShouldBeSameAs(theGraph.Options.Projections.AggregatorFor<IssueAggregate>());
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

    [Fact]
    public void default_append_mode_is_rich()
    {
        theGraph.AppendMode.ShouldBe(EventAppendMode.Rich);
        theGraph.EventAppender.ShouldBeOfType<RichEventAppender>();
    }

    [Fact]
    public void switch_to_quick()
    {
        theGraph.AppendMode = EventAppendMode.Quick;
        theGraph.EventAppender.ShouldBeOfType<QuickEventAppender>();
        theGraph.AppendMode.ShouldBe(EventAppendMode.Quick);
    }

    [Fact]
    public void switch_to_quick_and_back_to_rich()
    {
        theGraph.AppendMode = EventAppendMode.Quick;
        theGraph.AppendMode = EventAppendMode.Rich;
        theGraph.AppendMode.ShouldBe(EventAppendMode.Rich);
        theGraph.EventAppender.ShouldBeOfType<RichEventAppender>();
    }

    [Fact]
    public void use_identity_map_for_inline_aggregates_is_false_by_default()
    {
        theGraph.UseIdentityMapForInlineAggregates.ShouldBeFalse();
    }

    public class HouseRemodeling
    {
        public Guid Id { get; set; }
    }
}

public class IssueAggregate
{
    public Guid Id { get; set; }

    public void Apply(IssueAssigned e)
    {
        // Do stuff
    }
}
