using Marten.Events;
using Shouldly;

namespace Marten.Testing.Events
{
    public class EventMappingTests
    {
        public void event_name_for_event_type()
        {
            var mapping = new EventMapping(new StreamMapping(typeof(Quest)), typeof(MembersJoined));

            mapping.EventTypeName.ShouldBe("members_joined");
        }
    }
}