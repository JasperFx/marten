using Marten;
using Marten.Events;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class EventMappingTests
{
    [Fact]
    public void event_name_for_event_type()
    {
        var options = new StoreOptions();
        var mapping = new EventMapping<MembersJoined>(new EventGraph(options));

        mapping.EventTypeName.ShouldBe("members_joined");
    }
}