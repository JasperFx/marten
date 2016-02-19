using System;
using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class AggregateDefinitionTests
    {
        [Fact]
        public void derives_the_stream_type_name()
        {
            new AggregateConfiguration(typeof(HouseRemodeling)).StreamTypeName.ShouldBe("house_remodeling");
            new AggregateConfiguration(typeof(Quest)).StreamTypeName.ShouldBe("quest");
        }

        [Fact]
        public void has_event_type()
        {
            var mapping = new AggregateConfiguration(typeof(Quest));
            mapping.HasEventType(typeof(MembersJoined)).ShouldBeFalse();

            mapping.AddEvent(typeof (MembersJoined));

            mapping.HasEventType(typeof(MembersJoined)).ShouldBeTrue();

        }

        public class HouseRemodeling : IAggregate
        {
            public Guid Id { get; set; }
        }
    }
}