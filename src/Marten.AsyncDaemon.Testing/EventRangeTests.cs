using System.Collections.Generic;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Testing.Events.Aggregation;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class EventRangeTests
    {
        [Fact]
        public void size_with_no_events()
        {
            var range = new EventRange("name", 0, 100);
            range.Size.ShouldBe(100);
        }

        [Fact]
        public void size_with_events()
        {
            var range = new EventRange("name", 0, 100)
            {
                Events = new List<IEvent>
                {
                    new Event<AEvent>(new AEvent()),
                    new Event<AEvent>(new AEvent()),
                    new Event<AEvent>(new AEvent()),
                    new Event<AEvent>(new AEvent()),
                    new Event<AEvent>(new AEvent()),
                }
            };

            range.Size.ShouldBe(5);
        }
    }
}
