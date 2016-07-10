using System.Collections.Generic;
using Marten.Events.Projections.Async;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{
    public class EventPageTests
    {
        [Fact]
        public void perfectly_sequential()
        {
            var list = new List<long>() {3, 4, 5, 6, 7};

            EventPage.IsCompletelySequential(list)
                .ShouldBeTrue();
        }

        [Fact]
        public void not_sequential()
        {
            var list = new List<long>() { 3, 4, 5, 6, 7, 10 };

            EventPage.IsCompletelySequential(list)
                .ShouldBeFalse();
        }
    }
}