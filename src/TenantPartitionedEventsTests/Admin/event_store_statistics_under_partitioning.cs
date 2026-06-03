using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// #4617 section 3e — statistics + high-water APIs under
/// <c>UseTenantPartitionedEvents</c>. Pins what's correct (EventCount,
/// StreamCount, FetchMaxEventSequenceAsync) vs what's KNOWN-STALE
/// (FetchEventStoreStatistics.EventSequenceNumber, FetchHighestEventSequenceNumber
/// — both read the store-global mt_events_sequence, which is never advanced
/// under partitioning since per-tenant sequences are used instead).
///
/// <para>
/// Monitoring tools (CritterWatch, dashboards) that depend on EventSequenceNumber
/// as the event-store high-water mark have to switch to FetchMaxEventSequenceAsync
/// under partitioning. These tests pin BOTH sides so the contract is explicit.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class event_store_statistics_under_partitioning
{
    private readonly GuidPartitionedFixture _fixture;

    public event_store_statistics_under_partitioning(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FetchEventStoreStatistics_EventCount_and_StreamCount_reflect_appends_across_all_partitions()
    {
        // EventCount + StreamCount run `count(*)` against mt_events / mt_streams.
        // Under partitioning, `count(*)` scans every partition transparently, so
        // both should grow by the expected amount after we append events.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var db = (MartenDatabase)_fixture.Store.Storage.Database;
        var statsBefore = await db.FetchEventStoreStatistics(token: CancellationToken.None);

        // Append 1 stream, 4 events.
        var streamId = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.StartStream<TripSnapshot>(streamId,
                new TripStarted(streamId), new TripLeg(1), new TripLeg(2), new TripLeg(3));
            await session.SaveChangesAsync();
        }

        var statsAfter = await db.FetchEventStoreStatistics(token: CancellationToken.None);

        // Delta-based assertions — exact totals depend on sibling tests on the
        // shared store, but the relative growth must match what we just wrote.
        (statsAfter.EventCount - statsBefore.EventCount).ShouldBe(4L,
            "EventCount must reflect the 4 events this test appended (count(*) scans all partitions)");
        (statsAfter.StreamCount - statsBefore.StreamCount).ShouldBe(1L,
            "StreamCount must reflect the 1 stream this test started");
    }

    [Fact]
    public async Task FetchEventStoreStatistics_EventSequenceNumber_is_STALE_under_partitioning()
    {
        // The pin: the global mt_events_sequence is NEVER nextval'd under
        // partitioning (per-tenant sequences are used instead). The
        // EventSequenceNumber field of statistics reads `last_value` from that
        // dead global sequence, so it stays at its starting value regardless
        // of how many events have been appended store-wide.
        //
        // This is by-design — but monitoring tools that historically used
        // EventSequenceNumber as the event-store high-water need to switch to
        // FetchMaxEventSequenceAsync under partitioning. Pin the broken-by-design
        // value so the divergence is part of the documented contract.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        // Snapshot the global "high-water" BEFORE we append.
        var db = (MartenDatabase)_fixture.Store.Storage.Database;
        var globalBefore = await db.FetchHighestEventSequenceNumber(CancellationToken.None);
        var statsBefore = await db.FetchEventStoreStatistics(token: CancellationToken.None);

        // Now append 10 events to a fresh tenant.
        await _fixture.AppendNEventsAsync(tenant, 10);

        var globalAfter = await db.FetchHighestEventSequenceNumber(CancellationToken.None);
        var statsAfter = await db.FetchEventStoreStatistics(token: CancellationToken.None);

        // The global sequence is unchanged — pin the staleness.
        globalAfter.ShouldBe(globalBefore,
            "FetchHighestEventSequenceNumber reads the store-global sequence which is " +
            "NEVER advanced under per-tenant partitioning — stale by design");
        statsAfter.EventSequenceNumber.ShouldBe(statsBefore.EventSequenceNumber,
            "FetchEventStoreStatistics.EventSequenceNumber is the same dead value " +
            "as FetchHighestEventSequenceNumber");
    }

    [Fact]
    public async Task FetchMaxEventSequenceAsync_returns_the_correct_high_water_under_partitioning()
    {
        // FetchMaxEventSequenceAsync runs `select max(seq_id) from mt_events`,
        // which Postgres pushes down across every partition. That's the
        // monitoring-grade high-water mark to use under per-tenant partitioning,
        // and the spec calls it out as the replacement for the stale
        // FetchHighestEventSequenceNumber.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var db = (MartenDatabase)_fixture.Store.Storage.Database;
        var maxBefore = (await db.FetchMaxEventSequenceAsync(CancellationToken.None)) ?? 0L;

        // Append 3 events for the fresh tenant — its per-tenant sequence
        // starts at 1, so its max(seq_id) jumps to 3.
        await _fixture.AppendNEventsAsync(tenant, 3);

        var maxAfter = (await db.FetchMaxEventSequenceAsync(CancellationToken.None)) ?? 0L;

        // The store-global max(seq_id) must reflect at least the largest
        // per-tenant seq_id assigned in any partition. For this fresh tenant's
        // 3 events with seq_ids 1, 2, 3, the global max either stays at its
        // prior value (if some other tenant has higher seq_ids) or moves to 3 —
        // but in BOTH cases it must be > 0 once any event has been written.
        maxAfter.ShouldBeGreaterThan(0L,
            "FetchMaxEventSequenceAsync must return the global max(seq_id) across partitions");

        // After this tenant's 3 appends, the store-global max is at least
        // 3 — either from this tenant if no sibling has more, or higher if
        // a sibling tenant has already written more events.
        maxAfter.ShouldBeGreaterThanOrEqualTo(3L);

        // And critically: this MUST diverge from FetchHighestEventSequenceNumber
        // under partitioning — that's the whole point.
        var globalSeq = await db.FetchHighestEventSequenceNumber(CancellationToken.None);
        maxAfter.ShouldBeGreaterThan(globalSeq,
            "FetchMaxEventSequenceAsync must diverge from the stale FetchHighestEventSequenceNumber " +
            "under partitioning — this divergence IS the monitoring-grade pin");
    }
}
