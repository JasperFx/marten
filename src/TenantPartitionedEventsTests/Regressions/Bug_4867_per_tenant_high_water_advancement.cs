#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon.HighWater;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #4867 — under <c>UseTenantPartitionedEvents</c> a tenant's high-water mark froze permanently after
/// the first persisted <c>HighWaterMark:{tenant}</c> row: <c>loadPerTenantStatistics</c> seeded
/// <c>CurrentMark</c> straight from the persisted progression row whenever one existed (the tenant's
/// committed <c>max(seq_id)</c> was only consulted before the first persist), and the vectorized
/// per-tenant path had no advancement step at all. Any second batch of events for the tenant was
/// never projected.
///
/// <para>
/// This class pins the fixed detector contract directly (fast, detector-level, shared fixture):
/// per-tenant <c>CurrentMark</c> now advances from the persisted mark toward the tenant's committed
/// height through a per-tenant gap walk — the vectorized analogue of the store-global
/// <c>GapDetector</c> — holding at a gap (in-flight or rolled-back seq_id) until the tenant has been
/// stale past <c>StaleSequenceThreshold</c>, then skipping, mirroring <c>DetectInSafeZone</c>.
/// The daemon-level end-to-end regression lives in
/// <see cref="Bug_4867_second_batch_projects_under_continuous_daemon"/>.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class Bug_4867_per_tenant_high_water_advancement
{
    private readonly GuidPartitionedFixture _fixture;

    public Bug_4867_per_tenant_high_water_advancement(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    private HighWaterDetector newDetector() => new(
        (MartenDatabase)_fixture.Store.Storage.Database, _fixture.Store.Options.EventGraph,
        NullLogger.Instance);

    private async Task<HighWaterStatistics> detectAsync(IHighWaterDetector detector, string tenant)
    {
        var vector = await detector.DetectForTenantsAsync(new[] { tenant }, CancellationToken.None);
        vector.TryGetStatistics(tenant, out var statistics).ShouldBeTrue();
        return statistics;
    }

    // THE core detector-level regression: before the fix, CurrentMark stayed pinned to the persisted
    // progression row (6) forever; the second batch's committed height (11) was never reached.
    [Fact]
    public async Task current_mark_advances_from_the_persisted_row_to_the_committed_height()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        IHighWaterDetector detector = newDetector();

        // Batch 1: 6 events → seed reading (no progression row yet) = 6, then persist the mark the
        // way TenantedHighWaterCoordinator does after every poll.
        await _fixture.AppendNEventsAsync(tenant, 6);
        (await detectAsync(detector, tenant)).CurrentMark.ShouldBe(6);
        await detector.MarkHighWaterForTenantAsync(tenant, 6, CancellationToken.None);

        // Batch 2: 5 more committed events (seq 7..11, contiguous). The very next poll must advance —
        // no stale-threshold wait applies because there is no gap.
        await _fixture.AppendNEventsAsync(tenant, 5);

        var statistics = await detectAsync(detector, tenant);
        statistics.LastMark.ShouldBe(6, "LastMark reflects the persisted HighWaterMark:{tenant} row");
        statistics.HighestSequence.ShouldBe(11, "the tenant's committed max(seq_id)");
        statistics.CurrentMark.ShouldBe(11,
            "#4867: the per-tenant mark must advance from the persisted row to the committed height — " +
            "before the fix it stayed frozen at 6 and second batches never projected");
    }

    [Fact]
    public async Task gap_above_the_persisted_mark_holds_within_the_stale_threshold_then_skips_past_it()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        // The stale clock is detector-instance state (the per-tenant analogue of HighWaterAgent's
        // in-memory `_current`), so all polls in this test share one instance — exactly the daemon shape.
        IHighWaterDetector detector = newDetector();

        await _fixture.AppendNEventsAsync(tenant, 5);
        await detector.MarkHighWaterForTenantAsync(tenant, 5, CancellationToken.None);

        // Batch 2 = seq 6..10, then knock out seq 6 to simulate an allocated-but-never-committed
        // sequence number (in-flight append that rolled back) sitting right above the mark.
        await _fixture.AppendNEventsAsync(tenant, 5);
        await deleteEventAsync(tenant, 6);

        // Within the stale threshold the mark must HOLD at 5: max(seq_id) alone would say 10, but
        // advancing across the gap immediately could silently skip a lower seq that is still in flight.
        var held = await detectAsync(detector, tenant);
        held.CurrentMark.ShouldBe(5, "a gap immediately above the mark holds the mark within the threshold");
        held.IncludesSkipping.ShouldBeFalse();

        (await detectAsync(detector, tenant)).CurrentMark.ShouldBe(5,
            "an immediate second poll is still inside the stale threshold — no skip yet");

        // Once the tenant has been observably stuck past StaleSequenceThreshold, the detector skips
        // the gap to the tenant's safe-harbor sequence — the per-tenant mirror of DetectInSafeZone.
        var threshold = _fixture.Store.Options.Projections.StaleSequenceThreshold;
        await Task.Delay(threshold.Add(1500.Milliseconds()));

        var skipped = await detectAsync(detector, tenant);
        skipped.CurrentMark.ShouldBe(10,
            "past the stale threshold the tenant's mark skips the dead gap and lands on the committed height");
        skipped.IncludesSkipping.ShouldBeTrue();
    }

    [Fact]
    public async Task tenant_with_no_progression_row_still_seeds_from_committed_max_seq_id()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);
        await _fixture.AppendNEventsAsync(tenant, 3);

        // Pre-#4867 first-sighting behavior is intact: no HighWaterMark:{tenant} row yet → seed the
        // mark from the tenant's committed max(seq_id) so a fresh tenant's projections start at once.
        var statistics = await detectAsync(newDetector(), tenant);
        statistics.LastMark.ShouldBe(0);
        statistics.CurrentMark.ShouldBe(3);
        statistics.SafeStartMark.ShouldBe(3);
    }

    [Fact]
    public async Task a_persisted_mark_is_never_rewound_below_itself()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);
        await _fixture.AppendNEventsAsync(tenant, 3);

        // A mark ahead of the committed height (events archived/cleaned out from under it, or a
        // previously skipped-to position) must hold, never rewind.
        IHighWaterDetector detector = newDetector();
        await detector.MarkHighWaterForTenantAsync(tenant, 99, CancellationToken.None);

        var statistics = await detectAsync(detector, tenant);
        statistics.CurrentMark.ShouldBe(99, "never rewind a persisted per-tenant mark");
    }

    private async Task deleteEventAsync(string tenant, long seqId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.CreateCommand(
                $"delete from {_fixture.SchemaName}.mt_events where tenant_id = :t and seq_id = :s")
            .With("t", tenant)
            .With("s", seqId)
            .ExecuteNonQueryAsync();
    }
}

/// <summary>
/// #4867 daemon-level end-to-end regression: a continuous daemon under per-tenant partitioning must
/// keep projecting a tenant's SECOND batch of events after the first poll has persisted the
/// <c>HighWaterMark:{tenant}</c> row. This is the exact shape that froze — essentially every existing
/// test appended one batch per tenant before asserting, which is why the bug stayed hidden.
/// </summary>
public partial class Bug_4867_second_batch_projects_under_continuous_daemon
{
    public class Bug4867Trip { public Guid Id { get; set; } public double Distance { get; set; } }

    public record Bug4867Started(Guid Id);
    public record Bug4867Leg(double Distance);

    public partial class Bug4867TripProjection: SingleStreamProjection<Bug4867Trip, Guid>
    {
        public Bug4867TripProjection() => Name = "Bug4867Trip";
        public void Apply(Bug4867Trip a, Bug4867Leg e) => a.Distance += e.Distance;
    }

    private static DocumentStore buildStore(string schema)
    {
        return (DocumentStore)DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();

            o.Schema.For<Bug4867Trip>().DocumentAlias("b4867_trip");
            o.Projections.Add<Bug4867TripProjection>(ProjectionLifecycle.Async);
        });
    }

    [Fact]
    public async Task second_batch_for_a_tenant_projects_after_the_first_mark_is_persisted()
    {
        var schema = $"bug4867_a_p{Environment.ProcessId}";
        using var store = buildStore(schema);
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        const string tenant = "t4867_solo";
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        // Batch 1: 3 events (seq 1..3) BEFORE the daemon starts.
        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession(tenant))
        {
            session.Events.StartStream<Bug4867Trip>(streamId,
                new Bug4867Started(streamId), new Bug4867Leg(1.0), new Bug4867Leg(2.0));
            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        // Wait until batch 1 is projected AND the per-tenant high-water row has been PERSISTED —
        // the persisted row is exactly what froze the mark before the fix, so batch 2 must land after it.
        (await waitForAsync(30.Seconds(), async () =>
                await readDistanceAsync(store, tenant, streamId) == 3.0 &&
                await readTenantHighWaterAsync(schema, tenant) >= 3))
            .ShouldBeTrue("batch 1 must project and the HighWaterMark:{tenant} row must be persisted");

        // Batch 2: one more event (seq 4) while the daemon keeps running.
        await using (var session = store.LightweightSession(tenant))
        {
            session.Events.Append(streamId, new Bug4867Leg(5.0));
            await session.SaveChangesAsync();
        }

        // THE #4867 regression: before the fix the tenant's mark stayed frozen at 3 forever and this
        // wait timed out — the second batch was never projected.
        (await waitForAsync(30.Seconds(), async () =>
                await readDistanceAsync(store, tenant, streamId) == 8.0))
            .ShouldBeTrue(
                "#4867: the tenant's second batch must project — the per-tenant high-water mark must " +
                "advance past the first persisted HighWaterMark:{tenant} row");

        // The persisted row itself advances too (the coordinator persists after routing, so give it
        // its own short wait rather than racing the projection commit).
        (await waitForAsync(10.Seconds(), async () =>
                await readTenantHighWaterAsync(schema, tenant) == 4))
            .ShouldBeTrue("the persisted per-tenant mark advances to the second batch's committed height");

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task second_batches_advance_independently_for_an_active_and_a_lagging_tenant()
    {
        var schema = $"bug4867_b_p{Environment.ProcessId}";
        using var store = buildStore(schema);
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Active tenant (taller per-tenant sequence) + lagging tenant (shorter). The lagging tenant's
        // own appends never move the store-global max(seq_id) — its advancement must ride the shared
        // vectorized poll and its OWN committed height, fully independent of the active tenant's mark.
        const string active = "t4867_active";
        const string lagging = "t4867_lag";
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, active, lagging);

        var activeStream = Guid.NewGuid();
        var laggingStream = Guid.NewGuid();
        await using (var session = store.LightweightSession(active))
        {
            session.Events.StartStream<Bug4867Trip>(activeStream,
                new Bug4867Started(activeStream), new Bug4867Leg(1.0), new Bug4867Leg(1.0),
                new Bug4867Leg(1.0), new Bug4867Leg(1.0)); // 5 events → seq 1..5, distance 4.0
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession(lagging))
        {
            session.Events.StartStream<Bug4867Trip>(laggingStream,
                new Bug4867Started(laggingStream), new Bug4867Leg(1.0)); // 2 events → seq 1..2, distance 1.0
            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        (await waitForAsync(30.Seconds(), async () =>
                await readDistanceAsync(store, active, activeStream) == 4.0 &&
                await readDistanceAsync(store, lagging, laggingStream) == 1.0 &&
                await readTenantHighWaterAsync(schema, active) >= 5 &&
                await readTenantHighWaterAsync(schema, lagging) >= 2))
            .ShouldBeTrue("both tenants' first batches must project and both per-tenant marks must persist");

        // Second batches for both — the lagging tenant first (seq 3, well below the active tenant's
        // height of 5), then the active tenant (seq 6..7).
        await using (var session = store.LightweightSession(lagging))
        {
            session.Events.Append(laggingStream, new Bug4867Leg(10.0));
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession(active))
        {
            session.Events.Append(activeStream, new Bug4867Leg(20.0), new Bug4867Leg(20.0));
            await session.SaveChangesAsync();
        }

        (await waitForAsync(30.Seconds(), async () =>
                await readDistanceAsync(store, active, activeStream) == 44.0 &&
                await readDistanceAsync(store, lagging, laggingStream) == 11.0))
            .ShouldBeTrue(
                "#4867: each tenant's second batch must project independently — the lagging tenant " +
                "advances against its OWN committed height even though it never moves the store-global max");

        (await waitForAsync(10.Seconds(), async () =>
                await readTenantHighWaterAsync(schema, active) == 7 &&
                await readTenantHighWaterAsync(schema, lagging) == 3))
            .ShouldBeTrue("each persisted per-tenant mark lands on its own tenant's committed height");

        await daemon.StopAllAsync();
    }

    private static async Task<bool> waitForAsync(TimeSpan timeout, Func<Task<bool>> condition)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(250);
        }

        return await condition();
    }

    private static async Task<double?> readDistanceAsync(DocumentStore store, string tenant, Guid id)
    {
        await using var session = store.QuerySession(tenant);
        var doc = await session.LoadAsync<Bug4867Trip>(id);
        return doc?.Distance;
    }

    private static async Task<long> readTenantHighWaterAsync(string schema, string tenant)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var raw = await conn.CreateCommand(
                $"select last_seq_id from {schema}.mt_event_progression where name = :n")
            .With("n", $"HighWaterMark:{tenant}")
            .ExecuteScalarAsync();
        return raw is long v ? v : 0L;
    }
}
