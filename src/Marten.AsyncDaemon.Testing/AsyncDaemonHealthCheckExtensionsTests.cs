using System;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Marten.Events.Daemon.AsyncDaemonHealthCheckExtensions;

namespace Marten.AsyncDaemon.Testing;


public class AsyncDaemonHealthCheckExtensionsTests: DaemonContext
{

    private FakeHealthCheckBuilderStub builder = new();

    public AsyncDaemonHealthCheckExtensionsTests(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Fact]
    public void should_add_settings_to_services()
    {
        builder = new();
        builder.Services.ShouldNotContain(x => x.ServiceType == typeof(AsyncDaemonHealthCheckSettings));

        builder.AddMartenAsyncDaemonHealthCheck(200);

        builder.Services.ShouldContain(x => x.ServiceType == typeof(AsyncDaemonHealthCheckSettings));
    }

    [Fact]
    public void should_add_healthcheck_to_services()
    {
        builder = new();

        builder.AddMartenAsyncDaemonHealthCheck();

        var services = builder.Services.BuildServiceProvider();
        var healthCheckRegistrations = services.GetServices<HealthCheckRegistration>();
        healthCheckRegistrations.ShouldContain(reg => reg.Name == nameof(AsyncDaemonHealthCheck));
    }

    [Fact]
    public async Task should_be_healty_with_one_projection_no_relevant_events()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new FakeSingleStream1Projection(), ProjectionLifecycle.Async);
        });
        var agent = await StartDaemon();
        using var session = TheStore.LightweightSession();
        session.Events.Append(Guid.NewGuid(), new FakeIrrellevantEvent());
        await session.SaveChangesAsync();
        await agent.Tracker.WaitForHighWaterMark(1);
        var healthCheck = new AsyncDaemonHealthCheck(TheStore, new(100));

        var result = await healthCheck.CheckHealthAsync(new());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_be_unhealty_with_no_projection_lag_allowed()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new FakeSingleStream2Projection(), ProjectionLifecycle.Async);
        });
        var agent = await StartDaemon();
        using var session = TheStore.LightweightSession();
        var stream = Guid.NewGuid();
        var eventCount = 100;
        for (var i = 0; i < eventCount; i++)
            session.Events.Append(stream, new FakeEvent());
        await session.SaveChangesAsync();
        await agent.Tracker.WaitForHighWaterMark(eventCount);
        await agent.Tracker.WaitForShardState(new ShardState("FakeStream2:All", eventCount), 15.Seconds());
        var healthCheck = new AsyncDaemonHealthCheck(TheStore, new(0));

        var result = await healthCheck.CheckHealthAsync(new());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }


    [Fact]
    public async Task should_be_healty_with_all_projections_caught_up()
    {

        StoreOptions(x =>
            {
                x.Projections.Add(new FakeSingleStream3Projection(), ProjectionLifecycle.Async);
                x.Projections.Add(new FakeSingleStream4Projection(), ProjectionLifecycle.Async);
            });
        var agent = await StartDaemon();
        using var session = TheStore.LightweightSession();
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();
        var eventCount = 100;
        for (var i = 0; i < eventCount; i++)
        {
            session.Events.Append(stream1, new FakeEvent());
            session.Events.Append(stream2, new FakeEvent());
        }
        await session.SaveChangesAsync();
        await agent.Tracker.WaitForShardState(new ShardState("FakeStream3:All", eventCount), 15.Seconds());
        await agent.Tracker.WaitForShardState(new ShardState("FakeStream4:All", eventCount), 15.Seconds());
        await agent.Tracker.WaitForHighWaterMark(eventCount);
        var healthCheck = new AsyncDaemonHealthCheck(TheStore, new(1));

        var result = await healthCheck.CheckHealthAsync(new());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_be_unhealty_with_one_projection_lagging()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new FakeSingleStream5Projection(), ProjectionLifecycle.Async);
            x.Projections.Add(new FakeSingleStream6Projection(), ProjectionLifecycle.Async);
        });
        var agent = await StartDaemon();
        using var session = TheStore.LightweightSession();
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();
        var eventCount = 500;
        for (var i = 0; i < eventCount; i++)
        {
            session.Events.Append(stream1, new FakeEvent());
            session.Events.Append(stream2, new FakeEvent());
        }
        await session.SaveChangesAsync();
        await agent.Tracker.WaitForShardState(new ShardState("FakeStream5:All", eventCount), 15.Seconds());
        await agent.Tracker.WaitForShardState(new ShardState("FakeStream6:All", eventCount), 15.Seconds());
        await agent.Tracker.WaitForHighWaterMark(eventCount);
        using var treeCommand = new NpgsqlCommand($"update {TheStore.Events.DatabaseSchemaName}.mt_event_progression set last_seq_id = 0 where name = 'FakeStream6:All'", TheSession.Connection);
        await treeCommand.ExecuteScalarAsync();

        var healthCheck = new AsyncDaemonHealthCheck(TheStore, new(1));

        var result = await healthCheck.CheckHealthAsync(new());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }
}
