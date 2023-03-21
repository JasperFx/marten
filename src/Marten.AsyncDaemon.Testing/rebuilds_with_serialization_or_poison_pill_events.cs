using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class rebuilds_with_serialization_or_poison_pill_events: DaemonContext
{
    public rebuilds_with_serialization_or_poison_pill_events(ITestOutputHelper output): base(output)
    {
        SometimesFailingTripProjection.FailingEventFails = false;
        FailingEvent.SerializationFails = false;
    }

    [Fact]
    public async Task rebuild_the_projection_happy_path()
    {
        NumberOfStreams = 10;

        Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

        StoreOptions(x => x.Projections.Add(new SometimesFailingTripProjection(), ProjectionLifecycle.Async), true);

        var agent = await StartDaemon();

        await PublishSingleThreaded();

        var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

        await waiter;
        Logger.LogDebug("About to rebuild Trip:All");
        await agent.RebuildProjection("Trip", CancellationToken.None);
        Logger.LogDebug("Done rebuilding Trip:All");
        await CheckAllExpectedAggregatesAgainstActuals();
    }

    [Fact]
    public async Task rebuild_the_projection_skip_serialization_failures()
    {
        NumberOfStreams = 10;

        Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

        StoreOptions(x => x.Projections.Add(new SometimesFailingTripProjection(), ProjectionLifecycle.Async), true);

        var agent = await StartDaemon();

        await PublishSingleThreaded();

        var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

        await waiter;
        Logger.LogDebug("About to rebuild Trip:All");

        // Simulating serialization failures
        FailingEvent.SerializationFails = true;

        await agent.RebuildProjection("Trip", CancellationToken.None);
        Logger.LogDebug("Done rebuilding Trip:All");

        // Gotta do this, or the expected aggregation will fail w/ fake
        // serialization failures
        FailingEvent.SerializationFails = false;
        await CheckAllExpectedAggregatesAgainstActuals();

        var deadLetters = await theSession.Query<DeadLetterEvent>()
            .Where(x => x.ShardName == "All" && x.ProjectionName == "Trip")
            .ToListAsync();

        var badEventCount = Streams.SelectMany(x => x.Events).OfType<FailingEvent>().Count();
        deadLetters.Count.ShouldBe(badEventCount);
    }

    [Fact]
    public async Task rebuild_the_projection_skip_failed_events()
    {
        FailingEvent.SerializationFails = false;
        SometimesFailingTripProjection.FailingEventFails = true;

        NumberOfStreams = 5;

        Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

        StoreOptions(x =>
        {
            x.Projections.Add(new SometimesFailingTripProjection(), ProjectionLifecycle.Async);
            x.Projections.OnApplyEventException().SkipEvent();
        }, true);

        var agent = await StartDaemon();

        await PublishSingleThreaded();

        var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 60.Seconds());

        await waiter;
        Logger.LogDebug("About to rebuild Trip:All");
        await agent.RebuildProjection("Trip", CancellationToken.None);
        Logger.LogDebug("Done rebuilding Trip:All");

        // Gotta latch the failures so the aggregate checking can work here
        SometimesFailingTripProjection.FailingEventFails = false;
        await CheckAllExpectedAggregatesAgainstActuals();

        var deadLetters = await theSession.Query<DeadLetterEvent>().Where(x => x.ProjectionName == "Trip")
            .ToListAsync();

        var badEventCount = Streams.SelectMany(x => x.Events).OfType<FailingEvent>().Count();
        deadLetters.Count.ShouldBe(badEventCount);
    }
}

public class SometimesFailingTripProjection: TripProjectionWithCustomName
{
    public static bool FailingEventFails = false;


    public void Apply(FailingEvent e, Trip trip)
    {
        if (FailingEventFails) throw new InvalidOperationException("You shall not pass!");
    }
}

public class FailingEvent
{
    public static bool SerializationFails = false;

    public FailingEvent()
    {
        if (SerializationFails) throw new DivideByZeroException("Boom!");
    }
}
