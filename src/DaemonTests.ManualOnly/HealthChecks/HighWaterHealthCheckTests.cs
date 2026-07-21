using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Storage;
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

    private HighWaterHealthCheck buildCheck(TimeSpan? staleThreshold = null, long minimumGap = 1,
        bool autoRestart = false, IProjectionCoordinator? coordinator = null,
        Func<IMartenDatabase, bool>? databaseFilter = null, bool includeExternallyManaged = false)
    {
        var services = new ServiceCollection();
        if (coordinator != null)
        {
            services.AddSingleton(coordinator);
        }

        return new(theStore,
            new HighWaterHealthCheckSettings(staleThreshold ?? 30.Seconds(), minimumGap, autoRestart, databaseFilter,
                includeExternallyManaged), _timeProvider,
            _tracker, services.BuildServiceProvider());
    }

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

    // Seeds the HighWaterMark row's liveness heartbeat (jasperfx#539). Requires
    // EnableExtendedProgressionTracking so the `heartbeat` column exists.
    private async Task seedHighWaterHeartbeatAsync(long sequence, DateTimeOffset heartbeat)
    {
        var sql =
            $"insert into {theStore.Events.DatabaseSchemaName}.mt_event_progression (name, last_seq_id, last_updated, heartbeat) " +
            $"values ('HighWaterMark', {sequence}, now(), '{heartbeat:O}'::timestamptz) " +
            "on conflict (name) do update set last_seq_id = excluded.last_seq_id, heartbeat = excluded.heartbeat";
        await theSession.ExecuteAsync(new NpgsqlCommand(sql));
    }

    // Seeds an arbitrary progression row (store-global "HighWaterMark" or a per-tenant
    // "HighWaterMark:<tenant>" row) with an optional liveness heartbeat. The heartbeat column only
    // exists when EnableExtendedProgressionTracking is on, so pass a heartbeat only in that case.
    private async Task seedProgressionRowAsync(string name, long sequence, DateTimeOffset? heartbeat = null)
    {
        var sql = heartbeat is { } hb
            ? $"insert into {theStore.Events.DatabaseSchemaName}.mt_event_progression (name, last_seq_id, last_updated, heartbeat) " +
              $"values ('{name}', {sequence}, now(), '{hb:O}'::timestamptz) " +
              "on conflict (name) do update set last_seq_id = excluded.last_seq_id, heartbeat = excluded.heartbeat"
            : $"insert into {theStore.Events.DatabaseSchemaName}.mt_event_progression (name, last_seq_id, last_updated) " +
              $"values ('{name}', {sequence}, now()) " +
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

    // ---- heartbeat primary signal (marten#4986) ------------------------------------------

    [Fact]
    public async Task healthy_when_heartbeat_is_fresh_even_though_mark_is_behind()
    {
        // ExtendedProgression on -> the heartbeat is the primary signal. A fresh heartbeat means the
        // agent is cycling, so a mark sitting behind the latest event is NOT unhealthy (unlike the gap
        // heuristic, which would trip here).
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        await seedHighWaterHeartbeatAsync(1, _now.AddSeconds(-5)); // mark stuck at 1, but heartbeat is 5s old

        var result = await buildCheck(30.Seconds()).CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task unhealthy_when_heartbeat_is_stale_even_though_mark_is_caught_up()
    {
        // A stale heartbeat means the loop stopped cycling. This trips even when the mark is fully caught
        // up (gap == 0) — the case the gap heuristic is blind to.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        var stats = await theStore.Advanced.FetchEventStoreStatistics();
        await seedHighWaterHeartbeatAsync(stats.EventSequenceNumber, _now.AddSeconds(-90)); // caught up, heartbeat 90s old

        var result = await buildCheck(30.Seconds()).CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    // ---- autoRestart remediation (marten#4986) -------------------------------------------

    [Fact]
    public async Task autorestart_triggers_a_restart_once_and_still_reports_unhealthy()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        var stats = await theStore.Advanced.FetchEventStoreStatistics();
        await seedHighWaterHeartbeatAsync(stats.EventSequenceNumber, _now.AddSeconds(-90));

        var daemon = Substitute.For<IProjectionDaemon>();
        var coordinator = new FakeCoordinator(daemon);

        var check = buildCheck(30.Seconds(), autoRestart: true, coordinator: coordinator);

        // First stale cycle: restart the loop, still report Unhealthy so an alert fires.
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Unhealthy);
        await daemon.Received(1).RestartHighWaterAgentAsync(Arg.Any<CancellationToken>());

        // Second cycle inside the same staleness window: still Unhealthy, but NOT restarted again (capped).
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Unhealthy);
        await daemon.Received(1).RestartHighWaterAgentAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task without_autorestart_no_restart_is_attempted()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        var stats = await theStore.Advanced.FetchEventStoreStatistics();
        await seedHighWaterHeartbeatAsync(stats.EventSequenceNumber, _now.AddSeconds(-90));

        var daemon = Substitute.For<IProjectionDaemon>();
        var coordinator = new FakeCoordinator(daemon);

        var result = await buildCheck(30.Seconds(), coordinator: coordinator).CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        await daemon.DidNotReceive().RestartHighWaterAgentAsync(Arg.Any<CancellationToken>());
    }

    // ---- database scoping (marten#4991) --------------------------------------------------

    [Fact]
    public async Task database_filter_excluding_the_database_reports_healthy_despite_stuck_mark()
    {
        // marten#4991: a predicate that excludes this node's non-owned databases must stop the check
        // from probing (and asserting on) them at all — even a hard-stuck mark stays Healthy.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        var check = buildCheck(30.Seconds(), databaseFilter: _ => false);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        // still Healthy well past the staleness threshold, because the database is never probed
        _timeProvider.GetUtcNow().Returns(_now.AddSeconds(60));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task database_filter_including_the_database_still_detects_stuck_mark()
    {
        // The complementary case: a predicate that includes the database behaves exactly like no filter.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        var check = buildCheck(30.Seconds(), databaseFilter: _ => true);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        _timeProvider.GetUtcNow().Returns(_now.AddSeconds(60));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Unhealthy);
    }

    // ---- ExternallyManaged gate (marten#4991) --------------------------------------------

    [Fact]
    public async Task healthy_under_externally_managed_by_default_even_if_heartbeat_stale()
    {
        // Default: ExternallyManaged (e.g. Wolverine-managed distribution) hosts no local daemon, so the
        // check stays a no-op and a stale heartbeat is not asserted.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.ExternallyManaged;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        var stats = await theStore.Advanced.FetchEventStoreStatistics();
        await seedHighWaterHeartbeatAsync(stats.EventSequenceNumber, _now.AddSeconds(-90));

        var result = await buildCheck(30.Seconds()).CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task unhealthy_under_externally_managed_when_opted_in_and_heartbeat_stale()
    {
        // Opted in: assert under ExternallyManaged too — via the heartbeat signal.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.ExternallyManaged;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        var stats = await theStore.Advanced.FetchEventStoreStatistics();
        await seedHighWaterHeartbeatAsync(stats.EventSequenceNumber, _now.AddSeconds(-90));

        var result = await buildCheck(30.Seconds(), includeExternallyManaged: true)
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task externally_managed_opted_in_does_not_use_gap_fallback_without_heartbeat()
    {
        // Opted in but ExtendedProgression off -> no heartbeat. The gap fallback must be suppressed under
        // ExternallyManaged because an external owner can legitimately pause the mark; it stays Healthy.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.ExternallyManaged;
        });
        await appendEventsAsync(20);
        await seedHighWaterMarkAsync(1);

        var check = buildCheck(30.Seconds(), includeExternallyManaged: true);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        _timeProvider.GetUtcNow().Returns(_now.AddSeconds(60));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
    }

    // ---- per-tenant high water (marten#4991) ---------------------------------------------

    [Fact]
    public async Task detects_stale_per_tenant_high_water_via_heartbeat()
    {
        // UseTenantPartitionedEvents persists HighWaterMark:<tenant> rows rather than a single
        // store-global HighWaterMark. The original check matched only "HighWaterMark" and was blind to
        // a stalled per-tenant agent; now a stale per-tenant heartbeat is detected.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        await seedProgressionRowAsync("HighWaterMark:acme", 5, _now.AddSeconds(-90));

        var result = await buildCheck(30.Seconds()).CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task healthy_when_per_tenant_high_water_heartbeat_is_fresh()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
            x.Events.EnableExtendedProgressionTracking = true;
        });
        await appendEventsAsync(20);
        await seedProgressionRowAsync("HighWaterMark:acme", 5, _now.AddSeconds(-5));

        var result = await buildCheck(30.Seconds()).CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task per_tenant_high_water_without_heartbeat_is_not_gap_assessed()
    {
        // ExtendedProgression off -> a per-tenant row has no heartbeat, and there is no per-tenant
        // highest-sequence to compute a meaningful gap (FetchHighestEventSequenceNumber is store-global),
        // so the gap fallback must NOT run for it — otherwise a tenant with no new events false-positives.
        StoreOptions(x =>
        {
            x.Projections.Add(new HwFakeProjection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });
        await appendEventsAsync(20);
        await seedProgressionRowAsync("HighWaterMark:acme", 1);

        var check = buildCheck(30.Seconds());

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        _timeProvider.GetUtcNow().Returns(_now.AddSeconds(60));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
    }

    // Minimal IProjectionCoordinator that hands back a single daemon — avoids mocking a ValueTask-returning
    // member (CA2012) while letting the daemon itself stay an NSubstitute for Received(...) assertions.
    private sealed class FakeCoordinator: IProjectionCoordinator
    {
        private readonly IProjectionDaemon _daemon;

        public FakeCoordinator(IProjectionDaemon daemon) => _daemon = daemon;

        public IProjectionDaemon DaemonForMainDatabase() => _daemon;

        public ValueTask<IProjectionDaemon> DaemonForDatabase(string databaseIdentifier) => new(_daemon);

        public ValueTask<IReadOnlyList<IProjectionDaemon>> AllDaemonsAsync() =>
            new(new[] { _daemon });

        public Task PauseAsync() => Task.CompletedTask;

        public Task ResumeAsync() => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
