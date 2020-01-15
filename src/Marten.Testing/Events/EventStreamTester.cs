using System;
using System.Linq;
using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class EventStreamTester
    {
        [Fact]
        public void version_method_applies_in_the_right_order()
        {
            var stream = new EventStream(Guid.NewGuid(), false);

            stream.Add(new MembersJoined());
            stream.Add(new MembersJoined());
            stream.Add(new MembersDeparted());

            stream.ApplyLatestVersion(8);

            stream.Events.ElementAt(0).Version.ShouldBe(6);
            stream.Events.ElementAt(1).Version.ShouldBe(7);
            stream.Events.ElementAt(2).Version.ShouldBe(8);
        }
    }
}
