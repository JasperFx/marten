using Marten.Events.Daemon;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class ProjectionControllerTests
    {
        private AsyncOptions theOptions = new AsyncOptions {BatchSize = 500, MaximumHopperSize = 5000};
        private IProjectionUpdater theUpdater = Substitute.For<IProjectionUpdater>();
        private ProjectionController theController;

        public ProjectionControllerTests()
        {
            theController = new ProjectionController(new ShardName("the projection"), theUpdater, theOptions);
        }

        private void assertNoRangeWasEnqueued()
        {
            theUpdater.DidNotReceiveWithAnyArgs().StartRange(null);
        }

        private void assertRangeWasEnqueued(long floor, long ceiling)
        {
            var range = new EventRange(new ShardName("the projection"), floor, ceiling);
            theUpdater.Received().StartRange(range);
        }

        [Fact]
        public void starting_from_nothing()
        {
            theController.Start(0, 0);
            assertNoRangeWasEnqueued();
        }

        [Fact]
        public void starting_where_it_left_off()
        {
            theController.Start(150, 150);
            assertNoRangeWasEnqueued();
        }

        [Theory]
        [InlineData(100, 0)]
        [InlineData(400, 0)]
        [InlineData(150, 100)]
        [InlineData(250, 100)]
        [InlineData(600, 100)]
        public void start_less_than_or_equal_to_one_page_from_current(int highWaterMark, int current)
        {
            theController.Start(highWaterMark, current);
            assertRangeWasEnqueued(current, highWaterMark);
        }

        [Fact]
        public void high_water_mark_is_higher_than_starting_point_plus_batch_size()
        {
            theController.Start(1000, 100);
            assertRangeWasEnqueued(100, 600);
            assertRangeWasEnqueued(600, 1000);

            theController.InFlightCount.ShouldBe(900);
        }

        [Fact]
        public void high_water_mark_is_higher_than_starting_point_plus_batch_size_2()
        {
            theController.Start(1000, 0);
            assertRangeWasEnqueued(0, 500);
            assertRangeWasEnqueued(500, 1000);

            theController.InFlightCount.ShouldBe(1000);
        }

        [Fact]
        public void high_water_mark_is_much_higher_than_starting_point()
        {
            theController.Start(11000, 100);
            assertRangeWasEnqueued(100, 600);
            assertRangeWasEnqueued(600, 1100);
            assertRangeWasEnqueued(1100, 1600);

            theController.InFlightCount.ShouldBe(theOptions.MaximumHopperSize);
        }

        [Fact]
        public void mark_high_water_with_no_activity_small()
        {
            theController.Start(0, 0);
            theController.MarkHighWater(100);

            assertRangeWasEnqueued(0, 100);
        }

        [Fact]
        public void mark_high_water_with_no_activity_big()
        {
            theController.Start(0, 0);
            theController.MarkHighWater(theOptions.MaximumHopperSize + 5000);

            assertRangeWasEnqueued(0, 500);
            assertRangeWasEnqueued(500, 1000);

            theController.InFlightCount.ShouldBe(theOptions.MaximumHopperSize);
        }

        [Fact]
        public void mark_high_water_with_no_activity_really_big()
        {
            theController.Start(0, 0);
            theController.MarkHighWater(1200);

            assertRangeWasEnqueued(0, 500);
            assertRangeWasEnqueued(500, 1000);
            assertRangeWasEnqueued(1000, 1200);
        }

        [Fact]
        public void should_dequeue_in_flight_when_finished()
        {
            theController.Start(1200, 0);

            theController.EventRangeUpdated(new EventRange(new ShardName("the projection"), 0 ,500));
            theController.LastCommitted.ShouldBe(500);
            theController.InFlightCount.ShouldBe(700);
        }
    }
}
