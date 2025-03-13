using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Examples;
using EventSourcingTests.FetchForWriting;
using EventSourcingTests.Projections;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
            await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync([typeof(SimpleAggregate)], 5.Seconds(),
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

    [Fact]
    public async Task try_to_query_for_non_stale_data_by_aggregate_type()
    {
        var task = Task.Run(async () =>
        {
            using var session = theStore.LightweightSession();

            for (int i = 0; i < 20; i++)
            {
                session.Events.StartStream(new AEvent(), new BEvent());
                session.Events.StartStream(new CEvent(), new BEvent());
                session.Events.StartStream(new DEvent(), new AEvent());
                var streamId = session.Events.StartStream(new DEvent(), new CEvent());
                await session.SaveChangesAsync();
            }
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await Task.Delay(1.Seconds());

        var items = await theSession.QueryForNonStaleData<SimpleAggregate>(30.Seconds()).ToListAsync();
        items.Count.ShouldBeGreaterThan(0);

        await task;
    }

    public static async Task ExampleUsage()
    {
        #region sample_using_query_for_non_stale_data

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));
            opts.Projections.Add<TripProjection>(ProjectionLifecycle.Async);
        }).AddAsyncDaemon(DaemonMode.HotCold);

        using var host = builder.Build();
        await host.StartAsync();

        // DocumentStore() is an extension method in Marten just
        // as a convenience method for test automation
        await using var session = host.DocumentStore().LightweightSession();

        // This query operation will first "wait" for the asynchronous projection building the
        // Trip aggregate document to catch up to at least the highest event sequence number assigned
        // at the time this method is called
        var latest = await session.QueryForNonStaleData<Trip>(5.Seconds())
            .OrderByDescending(x => x.Started)
            .Take(10)
            .ToListAsync();

        #endregion
    }
}


