using System;
using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class AggregateStorageTests
    {
        [Fact]
        public void derives_the_stream_type_name()
        {
            throw new NotImplementedException("Redo on EventGraph");
            //new EventStreamStorage().StreamTypeName.ShouldBe("house_remodeling");
            //new EventStreamStorage().StreamTypeName.ShouldBe("quest");
        }

        [Fact]
        public void has_event_type()
        {
            throw new NotImplementedException("Redo on EventGraph");
            /*
            var mapping = new EventStreamStorage();
            mapping.HasEventType(typeof(MembersJoined)).ShouldBeFalse();

            mapping.AddEvent(typeof (MembersJoined));

            mapping.HasEventType(typeof(MembersJoined)).ShouldBeTrue();
            */

        }

        public class HouseRemodeling : IAggregate
        {
            public Guid Id { get; set; }
        }
    }
}