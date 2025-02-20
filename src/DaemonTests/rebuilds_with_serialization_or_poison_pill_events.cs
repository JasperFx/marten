using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

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

        StoreOptions(x =>
        {
            x.Projections.Add(new SometimesFailingTripProjection(), ProjectionLifecycle.Async);
        }, true);

        var agent = await StartDaemon();

        await PublishSingleThreaded();

        var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

        await waiter;
        Logger.LogDebug("About to rebuild Trip:All");
        await agent.RebuildProjectionAsync("Trip", CancellationToken.None);
        Logger.LogDebug("Done rebuilding Trip:All");
        await CheckAllExpectedAggregatesAgainstActuals();
    }

    [Fact]
    public async Task rebuild_the_projection_skip_serialization_failures()
    {
        NumberOfStreams = 10;

        Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

        StoreOptions(x =>
        {
            x.Projections.Add(new SometimesFailingTripProjection(), ProjectionLifecycle.Async);
            x.Projections.RebuildErrors.SkipSerializationErrors = true;
            x.Projections.Errors.SkipSerializationErrors = true;
        }, true);

        var agent = await StartDaemon();

        await PublishSingleThreaded();

        var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

        await waiter;
        Logger.LogDebug("About to rebuild Trip:All");

        // Simulating serialization failures
        FailingEvent.SerializationFails = true;

        await agent.RebuildProjectionAsync("Trip", CancellationToken.None);
        Logger.LogDebug("Done rebuilding Trip:All");

        // Gotta do this, or the expected aggregation will fail w/ fake
        // serialization failures
        FailingEvent.SerializationFails = false;
        await CheckAllExpectedAggregatesAgainstActuals();

        // Do this to force the dead letter queue to drain
        await agent.StopAllAsync();

        var deadLetters = await theSession.Query<DeadLetterEvent>()
            .Where(x => x.ShardName == "All" && x.ProjectionName == "Trip")
            .ToListAsync();

        var badEventCount = Streams.SelectMany(x => x.Events).OfType<FailingEvent>().Count();
        deadLetters.Count.ShouldBe(badEventCount);
    }


    [Theory]
    [InlineData(StorageConstants.DefaultTenantId)]
    [InlineData("CustomTenant")]
    public async Task rebuild_the_projection_skip_failed_events(string tenantId)
    {
        FailingEvent.SerializationFails = false;
        SometimesFailingTripProjection.FailingEventFails = true;

        NumberOfStreams = 5;
        UseTenant(tenantId);

        Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

        StoreOptions(x =>
        {
            if (tenantId != StorageConstants.DefaultTenantId)
            {
                x.Events.TenancyStyle = TenancyStyle.Conjoined;
                x.Policies.AllDocumentsAreMultiTenanted();
            }

            x.Projections.Add(new SometimesFailingTripProjection(), ProjectionLifecycle.Async);

            x.Projections.RebuildErrors.SkipApplyErrors = true;
        }, true);

        var agent = await StartDaemon(tenantId);

        await PublishSingleThreaded();

        var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 60.Seconds());

        await waiter;

        // Do this to force the dead letter queue to drain
        await agent.StopAllAsync();

        Logger.LogDebug("About to rebuild Trip:All");
        await agent.RebuildProjectionAsync("Trip", CancellationToken.None);
        Logger.LogDebug("Done rebuilding Trip:All");

        // Do this to force the dead letter queue to drain
        await agent.StopAllAsync();

        // Gotta latch the failures so the aggregate checking can work here
        SometimesFailingTripProjection.FailingEventFails = false;
        await CheckAllExpectedAggregatesAgainstActuals();

        var deadLetters = await theSession
            .Query<DeadLetterEvent>()
            .Where(x => x.ProjectionName == "Trip")
            .ToListAsync();

        var badEventCount = Streams.SelectMany(x => x.Events).OfType<FailingEvent>().Count();
        deadLetters.Count.ShouldBe(badEventCount);
    }
}

public class SometimesFailingTripProjection: TripProjectionWithCustomName
{
    public static bool FailingEventFails = false;

    public SometimesFailingTripProjection()
    {
        ProjectionName = "Trip";
    }

    public void Apply(IEvent<FailingEvent> e, Trip trip)
    {
        Debug.WriteLine("EVENT SEQUENCE WAS " + e.Sequence);
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
