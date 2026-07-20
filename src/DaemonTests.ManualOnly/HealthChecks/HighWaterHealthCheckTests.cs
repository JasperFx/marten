using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using NSubstitute;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Marten.Events.Daemon.HighWaterHealthCheckExtensions;

namespace DaemonTests.ManualOnly.HealthChecks;

public record HwFakeEvent();

public class HwFakeStream { public Guid Id { get; set; } }

public partial class HwFakeProjection: SingleStreamProjection<HwFakeStream, Guid>
{
    public void Apply(HwFakeEvent @event, HwFakeStream projection) { }
}

public class HighWaterHealthCheckTests: DaemonContext
{
    private FakeHealthCheckBuilderStub _builder = new();
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly HighWaterStateTracker _tracker = new();

    public HighWaterHealthCheckTests(ITestOutputHelper output): base(output)
    {
        _output = output;
        _timeProvider.GetUtcNow().Returns(_now);
    }

    private HighWaterHealthCheck buildCheck(TimeSpan? staleThreshold = null, long minimumGap = 1) =>
        new(theStore, new HighWaterHealthCheckSettings(staleThreshold ?? 30.Seconds(), minimumGap), _timeProvider,
            _tracker);

    private async Task appendEventsAsync(int count)
    {
        await using var session = theStore.LightweightSession();
        var stream = Guid.NewGuid();
        for (var i = 0; i < count; i++)
        {
            session.Events.Append(stream, new HwFakeEvent());
        }

        await session.SaveChangesAsync();
    }

    private async Task seedHighWaterMarkAsync(long sequence)
    {
        var sql =
            $"insert into {theStore.Events.DatabaseSchemaName}.mt_event_progression (name, last_seq_id, last_updated) " +
            $"values ('HighWaterMark', {sequence}, now()) " +
            "on conflict (name) do update set last_seq_id = excluded.last_seq_id";
        await theSession.ExecuteAsync(new NpgsqlCommand(sql));
    }

    // ---- registration --------------------------------------------------------------------

    [Fact]
    public void registers_settings_timeprovider_tracker_and_check()
    {
        _builder = new();
        _builder.AddMartenHighWaterHealthCheck(30.Seconds());

        _builder.Services.ShouldContain(x => x.ServiceType == typeof(HighWaterHealthCheckSettings));
        _builder.Services.ShouldContain(x => x.ServiceType == typeof(TimeProvider));
        _builder.Services.ShouldContain(x => x.ServiceType == typeof(HighWaterStateTracker));

        var services = _builder.Services.BuildServiceProvider();
        services.GetServices<HealthCheckRegistration>()
            .ShouldContain(reg => reg.Name == nameof(HighWaterHealthCheck));
    }

    // ---- gating --------------------------------------------------------------------------

    [Fact]
    public async Task healthy_when_no_async_projections_even_if_mark_is_stuck()
    {
        // no async projection registered -> the high-water agent runs nowhere, so a stuck mark is legitimate
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        var result = await buildCheck().CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task healthy_when_daemon_mode_is_disabled_even_if_mark_is_stuck()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Disabled;
        });
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        var result = await buildCheck().CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    // ---- signal --------------------------------------------------------------------------

    [Fact]
    public async Task healthy_when_mark_is_caught_up()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });
        await appendEventsAsync(20);
        var highest = await theStore.Advanced.FetchEventStoreStatistics();
        await seedHighWaterMarkAsync(highest.EventSequenceNumber);

        var result = await buildCheck().CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task healthy_within_grace_window_when_mark_first_seen_behind()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        // first observation of the gap only starts the clock; not yet stale
        var result = await buildCheck().CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task unhealthy_when_mark_stuck_behind_past_threshold()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        var check = buildCheck(30.Seconds());

        // first check records the stalled mark at _now
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        // advance the clock past the threshold with the mark still stuck
        _timeProvider.GetUtcNow().Returns(_now.AddSeconds(60));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task healthy_when_mark_advances_before_threshold()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        var check = buildCheck(30.Seconds());

        // first check: gap observed, clock starts
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        // the mark advances (agent alive) and time moves forward past the threshold
        await seedHighWaterMarkAsync(10);
        _timeProvider.GetUtcNow().Returns(_now.AddSeconds(60));

        // the advance resets the clock, so it must not be reported stale
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }
}
