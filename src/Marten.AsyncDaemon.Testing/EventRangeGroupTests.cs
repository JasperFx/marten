using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing;

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

    public override Task ConfigureUpdateBatch(ProjectionUpdateBatch batch)
    {
        throw new NotSupportedException();
    }

    public override ValueTask SkipEventSequence(long eventSequence, IMartenDatabase database)
    {
        throw new NotSupportedException();
    }
}
