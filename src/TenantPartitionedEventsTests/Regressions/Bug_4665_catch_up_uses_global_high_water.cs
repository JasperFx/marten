#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// Regression reproduction for
/// <see href="https://github.com/JasperFx/marten/issues/4665">#4665</see> —
/// <c>ForceAllMartenDaemonActivityToCatchUpAsync</c> /
/// <c>ProjectionDaemon.CatchUpAsync</c> never advances async projections to the
/// latest events under <c>UseTenantPartitionedEvents</c>.
///
/// <para>
/// <b>Root cause</b> (JasperFx-side): <c>JasperFxAsyncDaemon.CatchUpAsync</c>
/// uses the <i>store-global</i> <c>_highWater</c> agent
/// (<c>HighWaterAgent.CheckNowAsync</c> → <c>IHighWaterDetector.DetectInSafeZone</c>),
/// not the per-tenant <c>_tenantHighWater</c> coordinator the normally-running
/// daemon uses. Under <c>UseTenantPartitionedEvents</c>, each tenant has its
/// own <c>mt_events_sequence_{suffix}</c>; per-tenant <c>seq_id</c> values
/// interleave in <c>mt_events</c>, so the global <c>mt_events.seq_id</c>
/// presentation is permanently non-contiguous from the gap-detector's
/// perspective. <c>DetectInSafeZone</c> perpetually gap-skips, pinning
/// <c>CurrentMark</c> at <c>HighestSequence - 32</c> (#3865 buffer), and the
/// catch-up stops short of the just-appended events.
/// </para>
///
/// <para>
/// <b>Same family</b>: #4366 / #4598 / #4614 / #4596 — per-tenant partitioning
/// works at runtime (the normally-running daemon uses the vectorized
/// per-tenant high-water path) but the test-automation catch-up / wait
/// helpers assume a single contiguous global sequence.
/// </para>
///
/// <para>
/// <b>Expected</b>: <c>CatchUpAsync</c> dispatches on
/// <c>IHighWaterDetector.SupportsTenantPartitioning</c> — when true, it routes
/// through <c>_tenantHighWater</c> per the
/// <c>TenantedHighWaterCoordinator.PollAndRouteAsync</c> flow that the
/// running daemon uses (and that
/// <see cref="JasperFx.Events.Daemon.JasperFxAsyncDaemon"/>'s
/// <c>rebuildProjectionForTenant</c> already correctly uses for per-tenant
/// rebuilds). The Marten-side <c>HighWaterDetector</c> already exposes
/// <c>DetectForTenantsAsync</c> / <c>DetectInSafeZoneForTenantsAsync</c>
/// (#4596 Phase 2) — the fix is purely on the JasperFx caller side.
/// </para>
///
/// <para>
/// <b>Fix location</b>: <c>JasperFx.Events</c>
/// (<c>src/JasperFx.Events/Daemon/JasperFxAsyncDaemon.cs</c>:899 — the
/// no-timeout <c>CatchUpAsync(CancellationToken)</c> overload). This Marten
/// reproduction <see cref="Skip"/>'s itself with a JasperFx pin note until the
/// upstream fix ships.
/// </para>
///
/// <para>
/// <b>Reproduction shape</b>: build a fresh host with the user's reported
/// config — <c>UseTenantPartitionedEvents</c> + <c>TenancyStyle.Conjoined</c> +
/// async daemon Solo + a <c>SingleStreamProjection</c>. Add several tenants
/// and append events in a cross-tenant interleaving pattern so the global
/// <c>mt_events.seq_id</c> presentation gets the gap shape the bug fires on.
/// Stop the daemon (via <c>ForceAllMartenDaemonActivityToCatchUpAsync</c>'s
/// own internal stop+catch-up flow), then assert the projection reached the
/// latest event. Under the bug, the projection sits at
/// <c>HighestSequence - 32</c> and the last batch of events never lands.
/// </para>
/// </summary>
public partial class Bug_4665_catch_up_uses_global_high_water
{
    private readonly ITestOutputHelper _output;

    public Bug_4665_catch_up_uses_global_high_water(ITestOutputHelper output)
    {
        _output = output;
    }

    // Unique schema per process so net9 / net10 runs in the same DB don't
    // collide on the partition / sequence names that UseTenantPartitionedEvents
    // generates.
    private static readonly string SchemaName = $"bug4665_p{Environment.ProcessId}";

    public class TripDistance
    {
        public Guid Id { get; set; }
        public double Distance { get; set; }
        public int Version { get; set; }
    }

    public record TripStarted(Guid Id);
    public record TripLeg(double Distance);

    public partial class TripDistanceProjection: SingleStreamProjection<TripDistance, Guid>
    {
        public TripDistanceProjection()
        {
            Name = "Bug4665TripDistance";
        }

        public void Apply(TripDistance agg, TripLeg @event) => agg.Distance += @event.Distance;
    }

    [Fact(
        Skip = "#4665 — JasperFx-side fix pending. JasperFx.Events.Daemon.JasperFxAsyncDaemon.CatchUpAsync " +
               "(src/JasperFx.Events/Daemon/JasperFxAsyncDaemon.cs:899) calls _highWater.CheckNowAsync() and " +
               "HighWaterMark() (the store-global path) instead of dispatching on " +
               "IHighWaterDetector.SupportsTenantPartitioning to the _tenantHighWater coordinator the " +
               "normally-running daemon uses. Marten's HighWaterDetector already exposes " +
               "DetectForTenantsAsync / DetectInSafeZoneForTenantsAsync (#4596 Phase 2) — the fix is on " +
               "the JasperFx caller side. Locally this test hangs the catch-up loop (the global gap " +
               "detector waits indefinitely for sequence values that will never appear) which would " +
               "stall CI; we ship as Skip + repro until JasperFx ships and the JasperFx.Events version " +
               "bump in Directory.Packages.props lands.")]
    public async Task force_catch_up_advances_async_projection_under_partitioning()
    {
        // Store-direct shape (not host-based). The buggy method is
        // JasperFxAsyncDaemon.CatchUpAsync(CancellationToken) which we reach
        // by calling BuildProjectionDaemonAsync().CatchUpAsync(token) — the
        // same code path ForceAllMartenDaemonActivityToCatchUpAsync drives via
        // IProjectionCoordinator. Going store-direct removes the live daemon
        // that AddAsyncDaemon(Solo) would otherwise start in parallel and
        // catch up via the per-tenant high-water path BEFORE our buggy call
        // runs — masking the bug.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
        });
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Five tenants. Each gets its own mt_events_sequence_{suffix}; appends
        // across tenants therefore interleave in mt_events.seq_id and produce
        // the gap shape that the global HighWaterDetector's safe-zone walk
        // mis-reads as in-flight transactions.
        var tenants = Enumerable.Range(0, 5)
            .Select(_ => $"t_{Guid.NewGuid():N}".Substring(0, 12))
            .ToArray();
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        // Append cross-tenant interleaved: round-robin across tenants for 16
        // rounds, each round appends 1 TripStarted + 1 TripLeg per tenant.
        // 5 tenants × 16 rounds × 2 events = 160 events total — well past the
        // 32-event #3865 safe-zone buffer that masks the bug for tiny tests.
        var lastTripPerTenant = new Dictionary<string, Guid>();
        for (var round = 0; round < 16; round++)
        {
            foreach (var tenant in tenants)
            {
                var streamId = Guid.NewGuid();
                lastTripPerTenant[tenant] = streamId;
                await using var session = store.LightweightSession(tenant);
                session.Events.StartStream<TripDistance>(streamId,
                    new TripStarted(streamId),
                    new TripLeg(1.0));
                await session.SaveChangesAsync();
            }
        }

        // Induce the gap shape the reporter sees in a busy long-lived test
        // database: advance the global mt_events_sequence past every
        // per-tenant sequence's actual high value. Under partitioning the
        // global sequence is never used for appends, so in production it
        // stays at 1 — but a real bug-firing environment is one where the
        // global sequence has been pumped (by accumulated test runs that
        // toggled the flag, or by an upstream fixture sharing the schema)
        // beyond where mt_events.seq_id actually reaches. We simulate that
        // here so the bug fires reproducibly in a single-test run.
        //
        // Result of the bump: HighWaterDetector.loadCurrentStatistics reads
        // HighestSequence == big number while findCurrentMark looks at
        // mt_events for seq_id >= SafeStartMark (0). With mt_events.seq_id
        // values capped at the per-tenant ceiling (~32 here), the gap
        // detector finds max(seq_id) = ~32, but HighestSequence is far
        // larger. The mismatch trips the #3865 safe-zone branch where
        // CurrentMark = HighestSequence - 32, leaving every per-tenant
        // event UNDER the global advance unprojected.
        await using (var bumpConn = new Npgsql.NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await bumpConn.OpenAsync();
            await using var bumpCmd = bumpConn.CreateCommand();
            // Setval to 10,000 so the GLOBAL sequence reads far above the
            // actual per-tenant sequence high values (160 events across 5
            // tenants, max per-tenant ~32).
            bumpCmd.CommandText = $"select setval('{SchemaName}.mt_events_sequence', 10000);";
            await bumpCmd.ExecuteNonQueryAsync();
            await bumpConn.CloseAsync();
        }

        // Build a fresh daemon (not started) and drive its CatchUpAsync
        // directly — this IS the buggy JasperFx method. Under #4665 it
        // (a) calls _highWater.CheckNowAsync() which gap-skips on the
        // non-contiguous global mt_events.seq_id, then (b) catches each
        // shard up to HighWaterMark() which is pinned at
        // HighestSequence - 32. The result: the catch-up returns "success"
        // (no exception) but the tail of every per-tenant stream sits
        // unprojected.
        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await daemon.CatchUpAsync(cts.Token);
        }

        // Headline assertion: every tenant's LAST appended stream's projection
        // doc must exist and reflect the appended TripLeg (Distance == 1.0).
        // Under the bug, the projection for at least the most-recently-appended
        // tenant(s) is null — the catch-up stopped short before reaching the
        // tail of mt_events.
        var stale = new List<string>();
        foreach (var (tenant, streamId) in lastTripPerTenant)
        {
            await using var query = store.QuerySession(tenant);
            var doc = await query.LoadAsync<TripDistance>(streamId);
            if (doc is null || doc.Distance < 1.0)
            {
                stale.Add($"  tenant={tenant} streamId={streamId} doc={(doc is null ? "<null>" : $"Distance={doc.Distance}")}");
            }
        }

        stale.ShouldBeEmpty(
            $"async projection must advance to the latest events for every tenant after CatchUpAsync; " +
            $"under #4665 the global high-water gap-skip leaves the tail of the per-tenant streams unprojected. " +
            $"Stale tenants ({stale.Count} of {lastTripPerTenant.Count}):\n{string.Join("\n", stale)}");
    }
}
