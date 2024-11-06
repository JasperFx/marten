using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace DaemonTests;

[Collection("OneOffs")]
public class blue_green_projection_deployments
{
    [Fact]
    public void shard_name_uses_non_1_as_suffix_in_shard_name_identifier()
    {
        new ShardName("Baseline").Identity.ShouldBe("Baseline:All");
        new ShardName("Baseline", "All").Identity.ShouldBe("Baseline:All");
        new ShardName("Baseline", "All", 1).Identity.ShouldBe("Baseline:All");
        new ShardName("Baseline", "All", 2).Identity.ShouldBe("Baseline:V2:All");
    }

    [Fact]
    public async Task do_not_use_projection_version_as_part_of_shard_name_if_version_is_1()
    {
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add<BlueProjection>(ProjectionLifecycle.Async);
        });

        var names = store.Advanced.AllAsyncProjectionShardNames();
        names.Single().Identity.ShouldBe("Baseline:All");
    }

    [Fact]
    public async Task using_projection_version_as_part_of_shard_names_of_async_projections()
    {
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add<GreenProjection>(ProjectionLifecycle.Async);
        });

        var names = store.Advanced.AllAsyncProjectionShardNames();
        names.Single().Identity.ShouldBe("Baseline:V2:All");
    }

    [Fact]
    public async Task end_to_end()
    {
        await using var blueStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add<BlueProjection>(ProjectionLifecycle.Async);
            opts.DatabaseSchemaName = "bluegreen";
        });

        await using var greenStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add<GreenProjection>(ProjectionLifecycle.Async);
            opts.DatabaseSchemaName = "bluegreen";
        });

        using var blueDaemon = await blueStore.BuildProjectionDaemonAsync();
        await blueDaemon.StartAllAsync();

        using var greenDaemon = await greenStore.BuildProjectionDaemonAsync();
        await greenDaemon.StartAllAsync();

        var streamId = Guid.NewGuid();

        using (var session = blueStore.LightweightSession())
        {
            session.Events.StartStream<EventSourcingTests.Aggregation.MyAggregate>(streamId, new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent(),
                new EventSourcingTests.Aggregation.CEvent(), new EventSourcingTests.Aggregation.CEvent(), new EventSourcingTests.Aggregation.DEvent());
            await session.SaveChangesAsync();

            await blueDaemon.WaitForNonStaleData(5.Seconds());

            (await session.LoadAsync<EventSourcingTests.Aggregation.MyAggregate>(streamId)).ShouldBeEquivalentTo(new EventSourcingTests.Aggregation.MyAggregate
            {
                ACount = 2, BCount = 1, CCount = 3, DCount = 1, Version = 7, Id = streamId
            });
        }

        await greenDaemon.WaitForNonStaleData(5.Seconds());
        using (var session = greenStore.LightweightSession())
        {
            (await session.LoadAsync<EventSourcingTests.Aggregation.MyAggregate>(streamId)).ShouldBeEquivalentTo(new EventSourcingTests.Aggregation.MyAggregate
            {
                // The "green" version doubles the counts as a cheap way
                // of being able to test that the data is different
                ACount = 4, BCount = 2, CCount = 6, DCount = 2, Version = 7, Id = streamId
            });
        }
    }

    [Fact]
    public async Task test_through_host()
    {
        using var blueHost = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Projections.Add<BlueProjection>(ProjectionLifecycle.Async);
                    opts.DatabaseSchemaName = "bluegreen";
                }).AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();

        var blueStore = blueHost.Services.GetRequiredService<IDocumentStore>();

        using var greenHost = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Projections.Add<GreenProjection>(ProjectionLifecycle.Async);
                    opts.DatabaseSchemaName = "bluegreen";
                }).AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();

        var greenStore = greenHost.Services.GetRequiredService<IDocumentStore>();

        var blueDaemon = blueHost.Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();
        var greenDaemon = greenHost.Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();

        var streamId = Guid.NewGuid();

        using (var session = blueStore.LightweightSession())
        {
            session.Events.StartStream<EventSourcingTests.Aggregation.MyAggregate>(streamId, new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent(),
                new EventSourcingTests.Aggregation.CEvent(), new EventSourcingTests.Aggregation.CEvent(), new EventSourcingTests.Aggregation.DEvent());
            await session.SaveChangesAsync();

            await blueDaemon.WaitForNonStaleData(5.Seconds());

            (await session.LoadAsync<EventSourcingTests.Aggregation.MyAggregate>(streamId)).ShouldBeEquivalentTo(new EventSourcingTests.Aggregation.MyAggregate
            {
                ACount = 2, BCount = 1, CCount = 3, DCount = 1, Version = 7, Id = streamId
            });
        }

        await greenDaemon.WaitForNonStaleData(5.Seconds());
        using (var session = greenStore.LightweightSession())
        {
            (await session.LoadAsync<EventSourcingTests.Aggregation.MyAggregate>(streamId)).ShouldBeEquivalentTo(new EventSourcingTests.Aggregation.MyAggregate
            {
                // The "green" version doubles the counts as a cheap way
                // of being able to test that the data is different
                ACount = 4, BCount = 2, CCount = 6, DCount = 2, Version = 7, Id = streamId
            });
        }
    }
}

public class BlueProjection: SingleStreamProjection<EventSourcingTests.Aggregation.MyAggregate>
{
    public BlueProjection()
    {
        ProjectionName = "Baseline";
    }

    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.AEvent e) => aggregate.ACount++;
    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.BEvent e) => aggregate.BCount++;
    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.CEvent e) => aggregate.CCount++;
    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.DEvent e) => aggregate.DCount++;
}

public class GreenProjection: SingleStreamProjection<EventSourcingTests.Aggregation.MyAggregate>
{
    public GreenProjection()
    {
        ProjectionName = "Baseline";
        ProjectionVersion = 2;
    }

    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.AEvent e) => aggregate.ACount += 2;
    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.BEvent e) => aggregate.BCount += 2;
    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.CEvent e) => aggregate.CCount += 2;
    public void Apply(EventSourcingTests.Aggregation.MyAggregate aggregate, EventSourcingTests.Aggregation.DEvent e) => aggregate.DCount += 2;
}
