using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.New;
using Marten.Storage;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing.NewDaemon;

public class SubscriptionAgentTests
{
    private IEventLoader theLoader = Substitute.For<IEventLoader>();
    private AsyncOptions theOptions = new AsyncOptions();
    private ISubscriptionExecution theExecution = Substitute.For<ISubscriptionExecution>();
    private SubscriptionAgent theAgent;

    public SubscriptionAgentTests()
    {
        theAgent = new SubscriptionAgent("Something", theOptions, theLoader, theExecution);
    }

    [Fact]
    public async Task when_starting_and_the_high_water_is_zero_do_nothing()
    {
        await theAgent.Apply(Command.Started(0, 0));
        await theLoader.DidNotReceiveWithAnyArgs().LoadAsync(null, CancellationToken.None);
        theExecution.DidNotReceiveWithAnyArgs().Enqueue(null, theAgent);
    }

    [Fact]
    public async Task when_starting_and_the_last_committed_is_equal_to_the_high_water_mark_do_nothing()
    {
        await theAgent.Apply(Command.Started(5, 5));
        await theLoader.DidNotReceiveWithAnyArgs().LoadAsync(null, CancellationToken.None);
        theExecution.DidNotReceiveWithAnyArgs().Enqueue(null, theAgent);

        theAgent.HighWaterMark.ShouldBe(5);
        theAgent.LastCommitted.ShouldBe(5);

        // Move this up to
        theAgent.LastEnqueued.ShouldBe(5);
    }

    [Fact]
    public async Task when_starting_and_the_high_water_mark_is_non_zero_but_the_last_committed_is_zero()
    {
        var highWaterMark = 5;
        var lastCommitted = 0;

        var request = new EventRequest
        {
            HighWater = highWaterMark,
            BatchSize = theOptions.BatchSize,
            Floor = lastCommitted
        };

        // Little messy here
        var page = new EventPage(lastCommitted) { new Event<AEvent>(new AEvent()){Sequence = 4} };
        page.CalculateCeiling(theOptions.BatchSize, highWaterMark);

        theLoader.LoadAsync(request, theAgent.CancellationToken).Returns(page);

        await theAgent.Apply(Command.Started(highWaterMark, lastCommitted));

        theAgent.LastCommitted.ShouldBe(lastCommitted);
        theAgent.HighWaterMark.ShouldBe(highWaterMark);

        theAgent.LastEnqueued.ShouldBe(page.Ceiling);

        theExecution.Received().Enqueue(page, theAgent);
    }

    [Fact]
    public async Task when_starting_and_the_high_water_mark_is_non_zero_but_the_last_committed_is_zero_bigger_high_water()
    {
        var highWaterMark = 1000;
        var lastCommitted = 0;

        var expectedRequest = new EventRequest
        {
            HighWater = highWaterMark,
            BatchSize = theOptions.BatchSize,
            Floor = lastCommitted
        };

        // Little messy here
        var page = new EventPage(lastCommitted) { new Event<AEvent>(new AEvent()){Sequence = 4} };
        page.CalculateCeiling(theOptions.BatchSize, highWaterMark);

        theLoader.LoadAsync(expectedRequest, theAgent.CancellationToken).Returns(page);

        await theAgent.Apply(Command.Started(highWaterMark, lastCommitted));

        theAgent.LastCommitted.ShouldBe(lastCommitted);
        theAgent.HighWaterMark.ShouldBe(highWaterMark);

        theAgent.LastEnqueued.ShouldBe(page.Ceiling);

        theExecution.Received().Enqueue(page, theAgent);
    }

    [Fact]
    public async Task update_highwater_mark_when_otherwise_caught_up()
    {
        theAgent.HighWaterMark = 500;
        theAgent.LastCommitted = 500;
        theAgent.LastEnqueued = 500;

        var highWaterMark = 1000;

        var expectedRequest = new EventRequest
        {
            HighWater = highWaterMark,
            BatchSize = theOptions.BatchSize,
            Floor = 500
        };

        // Little messy here
        var page = new EventPage(500) { new Event<AEvent>(new AEvent()){Sequence = 698} };
        page.CalculateCeiling(theOptions.BatchSize, highWaterMark);

        theLoader.LoadAsync(expectedRequest, theAgent.CancellationToken).Returns(page);

        await theAgent.Apply(Command.HighWaterMarkUpdated(highWaterMark));
        theAgent.LastCommitted.ShouldBe(500); // no change
        theAgent.HighWaterMark.ShouldBe(highWaterMark);

        theAgent.LastEnqueued.ShouldBe(page.Ceiling);

        theExecution.Received().Enqueue(page, theAgent);
    }

    [Fact]
    public async Task update_highwater_mark_when_mostly_caught_up()
    {
        theAgent.HighWaterMark = 500;
        theAgent.LastCommitted = 400;
        theAgent.LastEnqueued = 400;

        var highWaterMark = 1000;


        var expectedRequest = new EventRequest
        {
            HighWater = highWaterMark,
            BatchSize = theOptions.BatchSize,
            Floor = 400
        };

        // Little messy here
        var page = new EventPage(500) { new Event<AEvent>(new AEvent()){Sequence = 698} };
        page.CalculateCeiling(theOptions.BatchSize, highWaterMark);

        theLoader.LoadAsync(expectedRequest, theAgent.CancellationToken).Returns(page);

        await theAgent.Apply(Command.HighWaterMarkUpdated(highWaterMark));
        theAgent.LastCommitted.ShouldBe(400); // no change
        theAgent.HighWaterMark.ShouldBe(highWaterMark);

        theAgent.LastEnqueued.ShouldBe(page.Ceiling);

        theExecution.Received().Enqueue(page, theAgent);
    }

    [Fact]
    public async Task update_highwater_mark_when_hopper_is_maxed_out()
    {
        theAgent.LastCommitted = 500;
        theAgent.LastEnqueued = theAgent.LastCommitted + theOptions.MaximumHopperSize;
        theAgent.HighWaterMark = theAgent.LastEnqueued;

        var highWaterMark = theAgent.HighWaterMark + 2500;

        await theAgent.Apply(Command.HighWaterMarkUpdated(highWaterMark));

        // Hold on, don't do anything else
        await theLoader.DidNotReceiveWithAnyArgs().LoadAsync(null, CancellationToken.None);
        theExecution.DidNotReceiveWithAnyArgs().Enqueue(null, theAgent);

        theAgent.LastCommitted.ShouldBe(500); // no change
        theAgent.HighWaterMark.ShouldBe(highWaterMark);
    }


    [Fact]
    public async Task update_highwater_mark_when_hopper_is_maxed_out_2()
    {
        theAgent.LastCommitted = 500;
        theAgent.LastEnqueued = theAgent.LastCommitted + theOptions.MaximumHopperSize;
        theAgent.HighWaterMark = theAgent.LastEnqueued + 20;


        var highWaterMark = theAgent.HighWaterMark + 2500;

        await theAgent.Apply(Command.HighWaterMarkUpdated(highWaterMark));

        // Hold on, don't do anything else
        await theLoader.DidNotReceiveWithAnyArgs().LoadAsync(null, CancellationToken.None);
        theExecution.DidNotReceiveWithAnyArgs().Enqueue(null, theAgent);

        theAgent.LastCommitted.ShouldBe(500); // no change
        theAgent.HighWaterMark.ShouldBe(highWaterMark);
    }

    [Fact]
    public async Task just_barely_bump_up_high_water_mark_hopper_is_small()
    {
        theAgent.HighWaterMark = 500;
        theAgent.LastCommitted = 400;
        theAgent.LastEnqueued = 400;

        theOptions.BatchSize = 500;
        var highWaterMark = 600;


        var expectedRequest = new EventRequest
        {
            HighWater = highWaterMark,
            BatchSize = theOptions.BatchSize,
            Floor = 400
        };

        // Little messy here
        var page = new EventPage(500) { new Event<AEvent>(new AEvent()){Sequence = 498} };
        page.CalculateCeiling(theOptions.BatchSize, highWaterMark);

        theLoader.LoadAsync(expectedRequest, theAgent.CancellationToken).Returns(page);

        await theAgent.Apply(Command.HighWaterMarkUpdated(highWaterMark));
        theAgent.LastCommitted.ShouldBe(400); // no change
        theAgent.HighWaterMark.ShouldBe(highWaterMark);

        theAgent.LastEnqueued.ShouldBe(page.Ceiling);

        theExecution.Received().Enqueue(page, theAgent);
    }

    [Fact]
    public async Task just_barely_bump_up_high_water_mark_hopper_is_large()
    {
        theAgent.LastCommitted = 7000;
        theAgent.LastEnqueued = theAgent.LastCommitted + theOptions.MaximumHopperSize;

        theAgent.HighWaterMark = theAgent.LastEnqueued + 100;

        theOptions.BatchSize = 500;

        var highWaterMark = theAgent.HighWaterMark + 25;

        await theAgent.Apply(Command.HighWaterMarkUpdated(highWaterMark));

        // Hold on, don't do anything else, let more events come in to batch up more
        await theLoader.DidNotReceiveWithAnyArgs().LoadAsync(null, CancellationToken.None);
        theExecution.DidNotReceiveWithAnyArgs().Enqueue(null, theAgent);

        theAgent.LastCommitted.ShouldBe(7000); // no change
        theAgent.HighWaterMark.ShouldBe(highWaterMark);
    }
}
