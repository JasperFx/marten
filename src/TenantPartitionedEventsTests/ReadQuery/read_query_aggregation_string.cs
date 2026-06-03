using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Linq;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.ReadQuery;

/// <summary>
/// #4617 section 3b — read / query / aggregation paths under
/// <c>UseTenantPartitionedEvents</c> for the string stream-identity flavor.
/// Mirrors the assertions in <see cref="read_query_aggregation_guid"/> against
/// <see cref="StringPartitionedFixture"/> so the read surfaces
/// (FetchStreamAsync, FetchStreamStateAsync, AggregateStreamAsync,
/// QueryAllRawEvents, QueryRawEventDataOnly, FetchLatest, LoadAsync) are
/// pinned under <c>StreamIdentity.AsString</c> as well as the default Guid
/// flavor. Same three invariants: (a) per-tenant isolation across every read
/// surface, (b) version-vs-sequence distinction (per-tenant seq_ids overlap
/// but per-stream versions are independent), (c) the current
/// non-tenant-scoped behavior of <c>LoadAsync(Guid eventId)</c>.
/// </summary>
[Collection("string-partitioned")]
public class read_query_aggregation_string
{
    private readonly StringPartitionedFixture _fixture;

    public read_query_aggregation_string(StringPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Unique-per-call string stream id. Hex-only, short, and prefixed so it
    /// reads as a stream id at a glance in failure messages — keeps the
    /// string flavor's id minting as cheap as <c>Guid.NewGuid()</c>.
    /// </summary>
    private static string NewStream() => "s_" + Guid.NewGuid().ToString("N")[..16];

    // ----- FetchStreamAsync isolation + ordering ------------------------------

    [Fact]
    public async Task FetchStreamAsync_returns_only_the_queried_tenants_events_in_version_order()
    {
        // Two tenants append the SAME stream id — under partitioning these are
        // two independent streams in separate partitions (the silent-split pin).
        // The FetchStreamAsync read path must scope by tenant_id; per-tenant
        // seq_ids legitimately overlap so a leak would surface as wrong-tenant
        // events showing up.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var streamId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId),
                new StringTripLeg(1), new StringTripLeg(2));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId),
                new StringTripLeg(10), new StringTripLeg(20), new StringTripLeg(30));
            await s.SaveChangesAsync();
        }

        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaEvents = await qa.Events.FetchStreamAsync(streamId);
        alphaEvents.Count.ShouldBe(3); // alpha's stream
        alphaEvents.Select(e => e.Version).ShouldBe(new long[] { 1, 2, 3 });

        await using var qb = _fixture.Store.QuerySession(beta);
        var betaEvents = await qb.Events.FetchStreamAsync(streamId);
        betaEvents.Count.ShouldBe(4); // beta's stream
        betaEvents.Select(e => e.Version).ShouldBe(new long[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task FetchStreamAsync_for_other_tenants_stream_returns_empty()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStreamId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(alphaStreamId, new StringTripStarted(alphaStreamId), new StringTripLeg(1));
            await s.SaveChangesAsync();
        }

        // Beta queries for alpha's stream id — empty: the read scopes by
        // (tenant_id, stream_id) and beta has no row for that id.
        await using var qb = _fixture.Store.QuerySession(beta);
        var betaSeesAlpha = await qb.Events.FetchStreamAsync(alphaStreamId);
        betaSeesAlpha.ShouldBeEmpty();
    }

    [Fact]
    public async Task FetchStreamAsync_version_bound_respected_under_partitioning()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId),
                new StringTripLeg(1), new StringTripLeg(2), new StringTripLeg(3), new StringTripLeg(4));
            await s.SaveChangesAsync();
        }

        await using var query = _fixture.Store.QuerySession(tenant);
        var twoEvents = await query.Events.FetchStreamAsync(streamId, version: 2);
        twoEvents.Count.ShouldBe(2);
        twoEvents.Last().Version.ShouldBe(2L);

        var fromV3 = await query.Events.FetchStreamAsync(streamId, version: 0, fromVersion: 3);
        fromV3.Count.ShouldBe(3); // versions 3, 4, 5
        fromV3.First().Version.ShouldBe(3L);
    }

    // ----- FetchStreamStateAsync isolation ------------------------------------

    [Fact]
    public async Task FetchStreamStateAsync_isolates_per_tenant_and_pins_version_not_sequence()
    {
        // FetchStreamStateAsync reads mt_streams; under partitioning each tenant
        // has its own partition row keyed by (tenant_id, id). Version = per-stream
        // count, INDEPENDENT of other tenants' per-tenant seq_ids. Pin both:
        // the per-tenant Version is what stream count expects, and a wrong-tenant
        // query returns null.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var sharedId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(sharedId, new StringTripStarted(sharedId), new StringTripLeg(1));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            // Beta drives the same id further along — 5 events.
            s.Events.StartStream<StringTripSnapshot>(sharedId, new StringTripStarted(sharedId),
                new StringTripLeg(1), new StringTripLeg(2), new StringTripLeg(3), new StringTripLeg(4));
            await s.SaveChangesAsync();
        }

        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaState = await qa.Events.FetchStreamStateAsync(sharedId);
        alphaState.ShouldNotBeNull();
        alphaState.Version.ShouldBe(2L); // alpha's stream has 2 events

        await using var qb = _fixture.Store.QuerySession(beta);
        var betaState = await qb.Events.FetchStreamStateAsync(sharedId);
        betaState.ShouldNotBeNull();
        betaState.Version.ShouldBe(5L); // beta's stream has 5 events

        // Wrong-tenant: a tenant with no row at this id returns null.
        var unrelated = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, unrelated);
        await using var qu = _fixture.Store.QuerySession(unrelated);
        var noState = await qu.Events.FetchStreamStateAsync(sharedId);
        noState.ShouldBeNull();
    }

    // ----- AggregateStreamAsync (live) ----------------------------------------

    [Fact]
    public async Task AggregateStreamAsync_live_is_built_only_from_the_tenants_events()
    {
        // Live aggregation reads the tenant's stream and folds it into the
        // aggregate via Apply(). Pin tenant-isolated reads + version bound +
        // overshoot returns null.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var streamId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId),
                new StringTripLeg(7), new StringTripLeg(11));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId), new StringTripLeg(99));
            await s.SaveChangesAsync();
        }

        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaAgg = await qa.Events.AggregateStreamAsync<StringTripSnapshot>(streamId);
        alphaAgg.ShouldNotBeNull();
        alphaAgg!.Distance.ShouldBe(18); // 7 + 11 — beta's 99 must NOT be folded in
        alphaAgg.LegCount.ShouldBe(2);

        // version bound = 2 (TripStarted + first TripLeg)
        var alphaAtV2 = await qa.Events.AggregateStreamAsync<StringTripSnapshot>(streamId, version: 2);
        alphaAtV2.ShouldNotBeNull();
        alphaAtV2!.Distance.ShouldBe(7);

        // overshoot — stream has 3 events; ask for version 99 → no events at that
        // version → null aggregate.
        var overshoot = await qa.Events.AggregateStreamAsync<StringTripSnapshot>(streamId, version: 99);
        overshoot.ShouldBeNull();
    }

    [Fact]
    public async Task AggregateStreamAsync_live_returns_null_for_unknown_stream()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        await using var query = _fixture.Store.QuerySession(tenant);
        var none = await query.Events.AggregateStreamAsync<StringTripSnapshot>(NewStream());
        none.ShouldBeNull();
    }

    // ----- QueryAllRawEvents tenant isolation ---------------------------------

    [Fact]
    public async Task QueryAllRawEvents_returns_only_the_querying_tenants_events_in_append_order()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStream = NewStream();
        var betaStream = NewStream();

        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(alphaStream, new StringTripStarted(alphaStream),
                new StringTripLeg(1), new StringTripLeg(2));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<StringTripSnapshot>(betaStream, new StringTripStarted(betaStream), new StringTripLeg(99));
            await s.SaveChangesAsync();
        }

        // Under StreamIdentity.AsString, IEvent.StreamId is unused (the actual
        // identifier lives in IEvent.StreamKey, which maps to mt_events.stream_id
        // as a varchar). Filter by StreamKey here — the guid flavor's StreamId
        // filter would not match anything meaningful in the string-identity table.
        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaAll = await qa.Events.QueryAllRawEvents()
            .Where(e => e.StreamKey == alphaStream)
            .OrderBy(e => e.Sequence)
            .ToListAsync();
        alphaAll.Count.ShouldBe(3); // alpha's 3 events
        alphaAll.Select(e => e.Version).ShouldBe(new long[] { 1, 2, 3 });

        // Beta's events from alpha's session: invisible. Counted by id since
        // the shared store may have other tests' events floating around.
        var alphaSeesBetaStream = await qa.Events.QueryAllRawEvents()
            .Where(e => e.StreamKey == betaStream)
            .ToListAsync();
        alphaSeesBetaStream.ShouldBeEmpty(
            "tenant-isolation: alpha's session must not see beta's events");
    }

    // ----- QueryRawEventDataOnly type-filter + tenant-filter ------------------

    [Fact]
    public async Task QueryRawEventDataOnly_filters_by_type_and_tenant()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStream = NewStream();
        var betaStream = NewStream();

        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(alphaStream, new StringTripStarted(alphaStream),
                new StringTripLeg(1), new StringTripLeg(2));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<StringTripSnapshot>(betaStream, new StringTripStarted(betaStream), new StringTripLeg(3));
            await s.SaveChangesAsync();
        }

        // Alpha's session queries StringTripLeg only — sees its own 2 legs, not beta's 1.
        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaLegs = await qa.Events.QueryRawEventDataOnly<StringTripLeg>()
            .Where(e => e.Distance < 100) // filter to keep this test's data scope, sibling tests don't write this exact shape
            .ToListAsync();
        alphaLegs.Count(l => l.Distance == 1 || l.Distance == 2).ShouldBe(2);
        alphaLegs.Any(l => l.Distance == 3).ShouldBeFalse(
            "tenant filter must scope by tenant_id — beta's StringTripLeg(3) cannot leak into alpha's session");
    }

    // ----- LoadAsync — pin the surprising cross-tenant behavior ---------------

    [Fact]
    public async Task LoadAsync_does_NOT_scope_by_tenant_pinning_current_behavior()
    {
        // SingleEventQueryHandler emits `where id = ?` only — no tenant predicate.
        // This means an event written under tenant B is loadable from tenant A's
        // session if you have the event id. Pin the current behavior so a future
        // tenant-aware change to LoadAsync is reviewed intentionally.
        // Event id is always Guid even under StreamIdentity.AsString — this test's
        // shape is identical to the guid flavor.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var betaStream = NewStream();
        Guid betaEventId;
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<StringTripSnapshot>(betaStream, new StringTripStarted(betaStream), new StringTripLeg(42));
            await s.SaveChangesAsync();
            // Re-read to capture the event id assigned by the bulk path.
            var events = await s.Events.FetchStreamAsync(betaStream);
            betaEventId = events.Last().Id;
        }

        // Alpha's session loads by id — current behavior returns beta's event.
        // This is "structural cross-tenant read" — surfaced as a known
        // limitation in the #4617 spec, not yet considered a bug.
        await using var qa = _fixture.Store.QuerySession(alpha);
        var loaded = await qa.Events.LoadAsync(betaEventId);
        loaded.ShouldNotBeNull(
            "LoadAsync currently has no tenant predicate — structural cross-tenant read. " +
            "If this changes (tenant-scoped LoadAsync), revisit the SingleEventQueryHandler comment");
    }

    // ----- FetchLatest — success-path oracle ----------------------------------

    [Fact]
    public async Task FetchLatest_returns_owning_tenants_aggregate_and_null_for_cross_tenant()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var streamId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId),
                new StringTripLeg(3), new StringTripLeg(4));
            await s.SaveChangesAsync();
        }

        // FetchLatest lives on IEventStoreOperations (LightweightSession), not on
        // the read-only IQueryEventStore — same surface either way for an idempotent
        // read.
        await using var sa = _fixture.Store.LightweightSession(alpha);
        var alphaLatest = await sa.Events.FetchLatest<StringTripSnapshot>(streamId);
        alphaLatest.ShouldNotBeNull();
        alphaLatest!.Distance.ShouldBe(7); // 3 + 4

        await using var sb = _fixture.Store.LightweightSession(beta);
        var betaLatest = await sb.Events.FetchLatest<StringTripSnapshot>(streamId);
        betaLatest.ShouldBeNull(
            "beta has no stream at this id — FetchLatest must return null, not leak alpha's aggregate");
    }

    // ----- Sequence vs Version semantics -------------------------------------

    [Fact]
    public async Task IEvent_Sequence_overlaps_across_tenants_but_Version_is_per_stream_independent()
    {
        // The defining invariant of per-tenant partitioning:
        //   - Per-tenant seq_id resets for each tenant (NewTenant() → fresh sequence)
        //     so both tenants' first events both have Sequence == 1.
        //   - Per-stream version is independent — each stream's first event has
        //     Version == 1 regardless of what other tenants have done.
        // This test pins BOTH halves of the invariant in one read path.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStream = NewStream();
        var betaStream = NewStream();
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(alphaStream, new StringTripStarted(alphaStream));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<StringTripSnapshot>(betaStream, new StringTripStarted(betaStream));
            await s.SaveChangesAsync();
        }

        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaFirst = (await qa.Events.FetchStreamAsync(alphaStream)).Single();
        await using var qb = _fixture.Store.QuerySession(beta);
        var betaFirst = (await qb.Events.FetchStreamAsync(betaStream)).Single();

        // Both first events have Version == 1 — per-stream version is independent.
        alphaFirst.Version.ShouldBe(1L);
        betaFirst.Version.ShouldBe(1L);

        // Both first events have Sequence == 1 — per-tenant sequence is independent
        // (each NewTenant() mints a fresh PG sequence whose first nextval is 1).
        // Cross-tenant Sequence collision is correct under partitioning.
        alphaFirst.Sequence.ShouldBe(1L);
        betaFirst.Sequence.ShouldBe(1L);
    }
}
