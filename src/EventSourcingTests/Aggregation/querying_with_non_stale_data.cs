using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Examples;
using EventSourcingTests.FetchForWriting;
using EventSourcingTests.Projections;
using JasperFx.Core;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class querying_with_non_stale_data : OneOffConfigurationsContext
{
    public querying_with_non_stale_data()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<LapMultiStreamProjection>(ProjectionLifecycle.Async);
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });
    }

    [Fact]
    public void can_find_the_shards_for_an_aggregate()
    {
        theStore.Options.Projections.AsyncShardsPublishingType(typeof(Lap))
            .Single().Identity.ShouldBe("Lap:All");

        theStore.Options.Projections.AsyncShardsPublishingType(typeof(SimpleAggregate))
            .Single().Identity.ShouldBe("SimpleAggregate:All");
    }

    [Fact]
    public async Task try_to_fetch_statistics_for_async_shards_smoke_tests()
    {
        theSession.Events.StartStream(new AEvent(), new BEvent());
        theSession.Events.StartStream(new CEvent(), new BEvent());
        theSession.Events.StartStream(new DEvent(), new AEvent());
        theSession.Events.StartStream(new DEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var progressions =
            await theStore.Storage.Database.FetchProjectionProgressFor([new ShardName("SimpleAggregate", "All"), ShardName.HighWaterMark]);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(5.Seconds());

        var all = await theStore.Storage.Database.AllProjectionProgress();
        all.Count.ShouldBe(3);

        progressions =
            await theStore.Storage.Database.FetchProjectionProgressFor([new ShardName("SimpleAggregate", "All"), ShardName.HighWaterMark]);


        progressions.Count.ShouldBe(2);

        foreach (var progression in progressions)
        {
            progression.Sequence.ShouldBeGreaterThan(0);
        }

    }

    [Fact]
    public async Task try_to_use_wait_for_non_stale_data_by_aggregate_type()
    {
        theSession.Events.StartStream(new AEvent(), new BEvent());
        theSession.Events.StartStream(new CEvent(), new BEvent());
        theSession.Events.StartStream(new DEvent(), new AEvent());
        theSession.Events.StartStream(new DEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var waiter = Task.Run(async () =>
            await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(typeof(SimpleAggregate), 5.Seconds(),
                CancellationToken.None));

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(5.Seconds());

        await waiter;

        var all = await theStore.Storage.Database.AllProjectionProgress();
        all.Count.ShouldBe(3);

        foreach (var progression in all)
        {
            progression.Sequence.ShouldBeGreaterThan(0);
        }

    }
}
