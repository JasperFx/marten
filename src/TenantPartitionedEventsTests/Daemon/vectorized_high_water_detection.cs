using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Daemon.HighWater;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_vectorized_high_water.cs
/// — #4596 Phase 2: vectorized per-tenant high-water + cross-tenant rebuild
/// source. These five tests are the CLEAN ones that run against the shared
/// per-tenant store; per-test isolation comes from unique
/// <see cref="PartitionedFixtureBase.NewTenant"/> ids. The two flag-OFF
/// fallback tests live in <see cref="vectorized_high_water_flag_off"/>.
///
/// <para>
/// <see cref="MartenDatabase.HighWaterDetector"/>'s
/// <c>DetectForTenantsAsync</c> / <c>DetectInSafeZoneForTenantsAsync</c>
/// overrides query each polled tenant's <c>mt_events_sequence_{suffix}</c> +
/// per-tenant high-water row in a single round-trip. Per-tenant
/// <c>HighestSequence</c> is per-tenant-relative — each tenant's
/// <c>mt_events_sequence_{suffix}.last_value</c> starts at 0 and tracks only
/// that tenant's appends — so seeding N events for a fresh tenant id and
/// asserting <c>HighestSequence == N</c> is correct under a shared store.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class vectorized_high_water_detection
{
    private readonly GuidPartitionedFixture _fixture;

    public vectorized_high_water_detection(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task detect_for_tenants_async_returns_one_reading_per_polled_tenant_in_one_roundtrip()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        var gamma = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta, gamma);

        // Heterogeneous tenant states: alpha gets 5 events, beta gets 2, gamma
        // gets none (its per-tenant sequence stays at 0 last_value).
        // AppendNEventsAsync writes one TripStarted + (count-1) TripLegs, so
        // count=5 → 5 events, count=2 → 2 events. Per-tenant sequences are
        // independent so each tenant's last_value equals exactly its event count.
        await _fixture.AppendNEventsAsync(alpha, 5);
        await _fixture.AppendNEventsAsync(beta, 2);

        var detector = new HighWaterDetector(
            (MartenDatabase)_fixture.Store.Storage.Database, _fixture.Store.Options.EventGraph, NullLogger.Instance);

        var vector = await ((IHighWaterDetector)detector).DetectForTenantsAsync(
            new[] { alpha, beta, gamma }, CancellationToken.None);

        vector.TenantCount.ShouldBe(3,
            "vectorized detector must emit one reading per polled tenant, even when the tenant has no events yet");

        vector.TryGetStatistics(alpha, out var alphaStat).ShouldBeTrue();
        vector.TryGetStatistics(beta, out var betaStat).ShouldBeTrue();
        vector.TryGetStatistics(gamma, out var gammaStat).ShouldBeTrue();

        alphaStat.TenantId.ShouldBe(alpha);
        betaStat.TenantId.ShouldBe(beta);
        gammaStat.TenantId.ShouldBe(gamma);

        // Per-tenant `HighestSequence` reflects each tenant's own
        // `mt_events_sequence_{suffix}.last_value` — that's the headline
        // independence signal. Gamma's reading is intact even though it has
        // no events (gap detection isn't stalled by gamma being flat).
        alphaStat.HighestSequence.ShouldBe(5L);
        betaStat.HighestSequence.ShouldBe(2L);
        gammaStat.HighestSequence.ShouldBe(0L);
    }

    [Fact]
    public async Task detect_for_tenants_async_returns_empty_vector_when_no_tenants_polled()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var detector = new HighWaterDetector(
            (MartenDatabase)_fixture.Store.Storage.Database, _fixture.Store.Options.EventGraph, NullLogger.Instance);

        var vector = await ((IHighWaterDetector)detector).DetectForTenantsAsync(
            new string[0], CancellationToken.None);

        vector.TenantCount.ShouldBe(0);
        vector.Global.ShouldBeNull();
    }

    // ---- VectorizedHighWaterMonitor integration with the Marten detector ----

    [Fact]
    public async Task monitor_polls_only_assigned_tenants_and_advances_ceiling_per_tenant()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        var gamma = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta, gamma);

        await _fixture.AppendNEventsAsync(alpha, 3);
        await _fixture.AppendNEventsAsync(beta, 1);

        var detector = new HighWaterDetector(
            (MartenDatabase)_fixture.Store.Storage.Database, _fixture.Store.Options.EventGraph, NullLogger.Instance);
        var monitor = new VectorizedHighWaterMonitor(detector);

        // PolledTenantSet starts empty — the daemon activates a tenant when one
        // of its shards lands on this node, deactivates when its last shard
        // leaves. The monitor only ever polls the currently-activated set.
        monitor.PolledTenants.Activate(alpha).ShouldBeTrue();
        monitor.PolledTenants.Activate(beta).ShouldBeTrue();
        // gamma intentionally NOT activated; should not appear in the poll.

        var readings = await monitor.PollAsync(CancellationToken.None);

        readings.Select(r => r.TenantId).OrderBy(t => t).ShouldBe(new[] { alpha, beta }.OrderBy(t => t));
        monitor.CeilingFor(alpha).ShouldBe(3L,
            "per-tenant rebuild ceiling = the alpha sequence's last_value");
        monitor.CeilingFor(beta).ShouldBe(1L);
        monitor.CeilingFor(gamma).ShouldBeNull(
            "gamma was never polled because it wasn't activated on this node — the monitor doesn't see it");
    }

    [Fact]
    public async Task polled_set_deactivate_removes_tenant_from_subsequent_polls()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);
        await _fixture.AppendNEventsAsync(alpha, 1);
        await _fixture.AppendNEventsAsync(beta, 1);

        var detector = new HighWaterDetector(
            (MartenDatabase)_fixture.Store.Storage.Database, _fixture.Store.Options.EventGraph, NullLogger.Instance);
        var monitor = new VectorizedHighWaterMonitor(detector);

        monitor.PolledTenants.Activate(alpha);
        monitor.PolledTenants.Activate(beta);

        (await monitor.PollAsync(CancellationToken.None)).Count.ShouldBe(2);

        // Deactivate alpha (simulates its last shard being redistributed off
        // this node by Wolverine).
        monitor.PolledTenants.Deactivate(alpha).ShouldBeTrue();

        var next = await monitor.PollAsync(CancellationToken.None);
        next.Select(r => r.TenantId).ShouldBe(new[] { beta });
    }

    [Fact]
    public async Task per_tenant_high_water_comes_from_max_seq_id_not_the_tenant_sequence()
    {
        // Same bug class as the store-global #4712 fix, but for the per-tenant reading: under sharded
        // tenancy the append path does NOT advance the per-tenant mt_events_sequence_{suffix} (events draw
        // their seq_id from the shared sequence, and BulkInsertEventsAsync reassigns per-tenant ranges
        // directly), so the sequence reports a stale value while the tenant's true height lives in
        // mt_events. DetectForTenantsAsync must derive HighestSequence from max(seq_id), NOT the sequence,
        // otherwise a fully-populated tenant reads as high-water 0 and its per-tenant projections never
        // start. Reproduce the stale sequence by force-resetting it after the append.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);
        await _fixture.AppendNEventsAsync(tenant, 7);

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            // last_value → 1 (is_called=false): what the sharded/global-sequence/bulk path leaves behind.
            await using var reset = new NpgsqlCommand(
                $"select setval('{_fixture.SchemaName}.mt_events_sequence_{tenant}', 1, false)", conn);
            await reset.ExecuteScalarAsync();
        }

        var detector = new HighWaterDetector(
            (MartenDatabase)_fixture.Store.Storage.Database, _fixture.Store.Options.EventGraph, NullLogger.Instance);

        var vector = await ((IHighWaterDetector)detector).DetectForTenantsAsync(
            new[] { tenant }, CancellationToken.None);

        vector.TryGetStatistics(tenant, out var stat).ShouldBeTrue();
        // Before the fix this read the stale sequence value (1); after it, the real committed height (7).
        stat.HighestSequence.ShouldBe(7);
    }

    // ---- ICrossTenantRebuildSource ----

    [Fact]
    public async Task find_rebuild_tenants_async_returns_every_registered_tenant_partition()
    {
        // Use unique tenant ids so the assertion can scope to OUR three tenants
        // even if sibling tests have registered more partitions on the shared store.
        var tenantA = PartitionedFixtureBase.NewTenant();
        var tenantB = PartitionedFixtureBase.NewTenant();
        var tenantC = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(
            CancellationToken.None, tenantA, tenantB, tenantC);

        var source = (ICrossTenantRebuildSource)_fixture.Store.Storage.Database;
        var tenants = await source.FindRebuildTenantsAsync("AnyProjection", CancellationToken.None);

        // Sibling tests on the shared store have also registered tenants — assert
        // ours are present, not that the list is exactly three.
        tenants.ShouldContain(tenantA);
        tenants.ShouldContain(tenantB);
        tenants.ShouldContain(tenantC);
    }
}
