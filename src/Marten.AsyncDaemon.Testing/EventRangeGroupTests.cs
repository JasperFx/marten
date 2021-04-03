using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class EventRangeGroupTests
    {
        private readonly TestEventRangeGroup theGroup = new TestEventRangeGroup(new EventRange(new ShardName("Trip", "All"), 100, 200));

        [Fact]
        public void initial_state()
        {
            theGroup.WasAborted.ShouldBeFalse();
            theGroup.Attempts.ShouldBe(-1);
        }

        [Fact]
        public void after_first_reset()
        {
            theGroup.Reset();
            theGroup.WasAborted.ShouldBeFalse();
            theGroup.Cancellation.IsCancellationRequested.ShouldBeFalse();
            theGroup.Attempts.ShouldBe(0);
        }

        [Fact]
        public void reset_and_abort()
        {
            theGroup.Reset();
            theGroup.Abort();

            theGroup.WasAborted.ShouldBeTrue();
            theGroup.Cancellation.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void reset_and_abort_and_reset_again()
        {
            theGroup.Reset();
            theGroup.Abort();
            theGroup.Reset();

            theGroup.WasAborted.ShouldBeFalse();
            theGroup.Cancellation.IsCancellationRequested.ShouldBeFalse();
            theGroup.Attempts.ShouldBe(1); // increment
        }

        [Fact]
        public void reset_and_abort_and_reset_again_with_exception()
        {
            theGroup.Reset();
            var exception = new DivideByZeroException();
            theGroup.Abort(exception);

            theGroup.Exception.ShouldBe(exception);

            theGroup.Reset();

            theGroup.Exception.ShouldBeNull();
        }
    }

    internal class TestEventRangeGroup: EventRangeGroup
    {
        public TestEventRangeGroup(EventRange range) : base(range, CancellationToken.None)
        {
        }

        protected override void reset()
        {
            WasReset = true;
        }

        public bool WasReset { get; set; }

        public override void Dispose()
        {
            // nothing
        }

        public override Task ConfigureUpdateBatch(IShardAgent shardAgent, ProjectionUpdateBatch batch,
            EventRangeGroup eventRangeGroup)
        {
            throw new System.NotImplementedException();
        }

        public override ValueTask SkipEventSequence(long eventSequence)
        {
            throw new System.NotImplementedException();
        }
    }
}
