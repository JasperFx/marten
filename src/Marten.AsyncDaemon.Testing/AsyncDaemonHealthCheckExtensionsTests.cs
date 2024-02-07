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
using NSubstitute;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Marten.Events.Daemon.AsyncDaemonHealthCheckExtensions;

namespace Marten.AsyncDaemon.Testing;


public class AsyncDaemonHealthCheckExtensionsTests: DaemonContext
{

    private FakeHealthCheckBuilderStub _builder = new();
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly DateTime _now = DateTime.UtcNow;

    public AsyncDaemonHealthCheckExtensionsTests(ITestOutputHelper output) : base(output)
    {
        _output = output;
        _timeProvider.GetUtcNow().Returns(_now);
    }

    [Fact]
    public void should_add_settings_to_services()
    {
        _builder = new();
        _builder.Services.ShouldNotContain(x => x.ServiceType == typeof(AsyncDaemonHealthCheckSettings));

        _builder.AddMartenAsyncDaemonHealthCheck(200);

        _builder.Services.ShouldContain(x => x.ServiceType == typeof(AsyncDaemonHealthCheckSettings));
    }

    [Fact]
    public void should_add_timeprovider_to_services()
    {
        _builder = new();
        _builder.Services.ShouldNotContain(x => x.ServiceType == typeof(TimeProvider));

        _builder.AddMartenAsyncDaemonHealthCheck(200, TimeSpan.FromSeconds(5));

        _builder.Services.ShouldContain(x => x.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void should_add_healthcheck_to_services()
    {
        _builder = new();

        _builder.AddMartenAsyncDaemonHealthCheck();

        var services = _builder.Services.BuildServiceProvider();
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
        using var session = theStore.LightweightSession();
        session.Events.Append(Guid.NewGuid(), new FakeIrrellevantEvent());
        await session.SaveChangesAsync();
        await agent.Tracker.WaitForHighWaterMark(1);
        var healthCheck = new AsyncDaemonHealthCheck(theStore, new(100), _timeProvider);

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
        using var session = theStore.LightweightSession();
        var stream = Guid.NewGuid();
        var eventCount = 100;
        for (var i = 0; i < eventCount; i++)
            session.Events.Append(stream, new FakeEvent());
        await session.SaveChangesAsync();
        await agent.Tracker.WaitForHighWaterMark(eventCount);
        await agent.Tracker.WaitForShardState(new ShardState("FakeStream2:All", eventCount), 15.Seconds());
        var healthCheck = new AsyncDaemonHealthCheck(theStore, new(0), _timeProvider);

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
        using var session = theStore.LightweightSession();
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
        var healthCheck = new AsyncDaemonHealthCheck(theStore, new(1), _timeProvider);

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
        using var session = theStore.LightweightSession();
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

        using var treeCommand = new NpgsqlCommand($"update {theStore.Events.DatabaseSchemaName}.mt_event_progression set last_seq_id = 0 where name = 'FakeStream6:All'");

        await theSession.ExecuteAsync(treeCommand);

        var healthCheck = new AsyncDaemonHealthCheck(theStore, new(1), _timeProvider);

        var result = await healthCheck.CheckHealthAsync(new());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task should_be_healthy_with_one_projection_lagging_but_within_max_same_lag_time()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new FakeSingleStream5Projection(), ProjectionLifecycle.Async);
            x.Projections.Add(new FakeSingleStream6Projection(), ProjectionLifecycle.Async);
        });
        var agent = await StartDaemon();
        using var session = theStore.LightweightSession();
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

        using var treeCommand = new NpgsqlCommand($"update {theStore.Events.DatabaseSchemaName}.mt_event_progression set last_seq_id = 0 where name = 'FakeStream6:All'");

        await theSession.ExecuteAsync(treeCommand);

        var healthCheck = new AsyncDaemonHealthCheck(theStore, new(1, TimeSpan.FromSeconds(30)), _timeProvider);

        var result = await healthCheck.CheckHealthAsync(new());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_be_unhealthy_with_one_projection_lagging_for_more_than_max_same_lag_time()
    {
        // Given
        StoreOptions(x =>
        {
            x.Projections.Add(new FakeSingleStream5Projection(), ProjectionLifecycle.Async);
            x.Projections.Add(new FakeSingleStream6Projection(), ProjectionLifecycle.Async);
        });
        var agent = await StartDaemon();
        using var session = theStore.LightweightSession();
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

        using var treeCommand = new NpgsqlCommand($"update {theStore.Events.DatabaseSchemaName}.mt_event_progression set last_seq_id = 0 where name = 'FakeStream6:All'");

        await theSession.ExecuteAsync(treeCommand);

        var maxSameLagTime = TimeSpan.FromSeconds(30);
        var healthCheck = new AsyncDaemonHealthCheck(theStore, new(1, maxSameLagTime), _timeProvider);
        await healthCheck.CheckHealthAsync(new());

        // When
        var afterMaxSameLagTime = _now.Add(maxSameLagTime.Add(TimeSpan.FromMilliseconds(1)));
        _timeProvider.GetUtcNow().Returns(afterMaxSameLagTime);
        var result = await healthCheck.CheckHealthAsync(new());

        // Then
        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task should_be_healthy_with_one_projection_lagging_for_more_than_max_same_lag_time_but_progressing()
    {
        // Given
        StoreOptions(x =>
        {
            x.Projections.Add(new FakeSingleStream5Projection(), ProjectionLifecycle.Async);
            x.Projections.Add(new FakeSingleStream6Projection(), ProjectionLifecycle.Async);
        });
        var agent = await StartDaemon();
        using var session = theStore.LightweightSession();
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

        await using var treeCommandSeqId0 = new NpgsqlCommand($"update {theStore.Events.DatabaseSchemaName}.mt_event_progression set last_seq_id = 0 where name = 'FakeStream6:All'");
        await theSession.ExecuteAsync(treeCommandSeqId0);

        var maxSameLagTime = TimeSpan.FromSeconds(30);
        var healthCheck = new AsyncDaemonHealthCheck(theStore, new(1, maxSameLagTime), _timeProvider);
        await healthCheck.CheckHealthAsync(new());

        // When
        await using var treeCommandSeqId1 = new NpgsqlCommand($"update {theStore.Events.DatabaseSchemaName}.mt_event_progression set last_seq_id = 1 where name = 'FakeStream6:All'");
        await theSession.ExecuteAsync(treeCommandSeqId1);

        var afterMaxSameLagTime = _now.Add(maxSameLagTime.Add(TimeSpan.FromMilliseconds(1)));
        _timeProvider.GetUtcNow().Returns(afterMaxSameLagTime);
        var result = await healthCheck.CheckHealthAsync(new());

        // Then
        result.Status.ShouldBe(HealthStatus.Healthy);
    }
}
