using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class EventMappingTests
    {
        [Fact]
        public void event_name_for_event_type()
        {
            var options = new StoreOptions();
            var mapping = new EventMapping<MembersJoined>(new EventGraph(options));

            mapping.EventTypeName.ShouldBe("members_joined");
        }

        [Fact]
        public void arbitrary_event_name_for_event_type()
        {
            var options = new StoreOptions();
            var mapping = new EventMapping<MembersJoined>(new EventGraph(options), "special_members_joined");

            mapping.EventTypeName.ShouldBe("special_members_joined");
        }
    }
}