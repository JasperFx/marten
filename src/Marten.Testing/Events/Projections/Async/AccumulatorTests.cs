using Marten.Events;
using Marten.Events.Projections.Async;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{

    public static class EventMother
    {
        public static IEvent[] Random(int count)
        {
            var events = new IEvent[count];
            for (int i = 0; i < events.Length; i++)
            {
                events[i] = new Event<MembersJoined>(new MembersJoined{Day = i});
            }

            return events;
        }
    }

    public class AccumulatorTests
    {
        private readonly Accumulator theAccumulator = new Accumulator();

        [Fact]
        public void initial_state()
        {
            theAccumulator.First.ShouldBeNull();
            theAccumulator.Last.ShouldBeNull();

            theAccumulator.CachedEventCount.ShouldBe(0);
        }

        private EventPage toPage(long from, long to, int eventCount)
        {
            return new EventPage(from, to, EventMother.Random(eventCount));
        }

        [Fact]
        public void store_the_very_first_page()
        {
            var page = toPage(1, 100, 98);

            theAccumulator.Store(page);

            theAccumulator.First.ShouldBeTheSameAs(page);
            theAccumulator.Last.ShouldBeTheSameAs(page);

            theAccumulator.CachedEventCount.ShouldBe(98);
        }

        [Fact]
        public void store_the_second_page()
        {
            var page1 = toPage(1, 100, 98);
            var page2 = toPage(101, 200, 88);

            theAccumulator.Store(page1);
            theAccumulator.Store(page2);

            theAccumulator.First.ShouldBe(page1);
            theAccumulator.Last.ShouldBe(page2);

            theAccumulator.CachedEventCount.ShouldBe(98 + 88);
        }

        [Fact]
        public void store_a_bunch_of_pages()
        {
            var page1 = toPage(1, 100, 98);
            var page2 = toPage(101, 200, 88);
            var page3 = toPage(201, 300, 77);
            var page4 = toPage(301, 400, 66);

            theAccumulator.Store(page1);
            theAccumulator.Store(page2);
            theAccumulator.Store(page3);
            theAccumulator.Store(page4);

            theAccumulator.First.ShouldBe(page1);
            theAccumulator.Last.ShouldBe(page4);

            page1.Next.ShouldBe(page2);
            page2.Next.ShouldBe(page3);
            page3.Next.ShouldBe(page4);
        }

        [Fact]
        public void prune_total_miss()
        {
            var page2 = toPage(101, 200, 88);
            var page3 = toPage(201, 300, 77);
            var page4 = toPage(301, 400, 66);

            theAccumulator.Store(page2);
            theAccumulator.Store(page3);
            theAccumulator.Store(page4);

            theAccumulator.Prune(99);

            theAccumulator.AllPages().ShouldHaveTheSameElementsAs(page2, page3, page4);
        }

        [Fact]
        public void prune_partial_miss()
        {
            var page2 = toPage(101, 200, 88);
            var page3 = toPage(201, 300, 77);
            var page4 = toPage(301, 400, 66);

            theAccumulator.Store(page2);
            theAccumulator.Store(page3);
            theAccumulator.Store(page4);

            theAccumulator.Prune(150);

            theAccumulator.AllPages().ShouldHaveTheSameElementsAs(page2, page3, page4);
        }

        [Fact]
        public void prune_hit()
        {
            var page1 = toPage(1, 100, 98);
            var page2 = toPage(101, 200, 88);
            var page3 = toPage(201, 300, 77);
            var page4 = toPage(301, 400, 66);

            theAccumulator.Store(page1);
            theAccumulator.Store(page2);
            theAccumulator.Store(page3);
            theAccumulator.Store(page4);

            theAccumulator.Prune(200);

            theAccumulator.AllPages().ShouldHaveTheSameElementsAs(page3, page4);
        }
    }
}