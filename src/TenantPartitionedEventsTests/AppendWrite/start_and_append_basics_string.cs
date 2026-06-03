using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.AppendWrite;

/// <summary>
/// #4617 section 3a — foundational write-path semantics under
/// <c>UseTenantPartitionedEvents</c> for the string stream-identity flavor.
/// Covers plain <c>StartStream</c> + plain <c>Append</c> happy paths, per-tenant
/// sequence + version contiguity, same-stream-key-cross-tenant isolation (the
/// silent-split that's correct under partitioning), archived-stream rejection
/// (MT001 → <see cref="InvalidStreamOperationException"/>), and the
/// duplicate-key-within-tenant collision shape. Guid identity parallel lives
/// in <see cref="start_and_append_basics_guid"/>.
/// </summary>
[Collection("string-partitioned")]
public class start_and_append_basics_string
{
    private readonly StringPartitionedFixture _fixture;

    public start_and_append_basics_string(StringPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Mint a unique stream key for a string-identity store. Lowercase,
    /// hyphen-free, leading-letter-safe — same conventions as the
    /// <see cref="PartitionedFixtureBase.NewTenant"/> tenant-id helper. Short
    /// (18 chars) so it never pushes generated identifiers past PG's 63-byte
    /// cap when concatenated with schema / table prefixes.
    /// </summary>
    private static string NewStream() => "s_" + Guid.NewGuid().ToString("N")[..16];

    [Fact]
    public async Task StartStream_plain_assigns_contiguous_versions_and_per_tenant_seq_ids()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = NewStream();
        await using var session = _fixture.Store.LightweightSession(tenant);
        session.Events.StartStream<StringTripSnapshot>(streamId,
            new StringTripStarted(streamId),
            new StringTripLeg(1),
            new StringTripLeg(2));
        await session.SaveChangesAsync();

        await using var query = _fixture.Store.QuerySession(tenant);
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(3);

        // Per-stream version is contiguous 1..N regardless of the per-tenant
        // sequence's prior state.
        events.Select(e => e.Version).ShouldBe(new long[] { 1, 2, 3 });

        // Per-tenant seq_ids are strictly increasing within this stream's batch
        // (no inter-test seq_id values assumed — sequence is shared across tests
        // within a tenant but each NewTenant() is fresh).
        var seqs = events.Select(e => e.Sequence).ToArray();
        for (var i = 1; i < seqs.Length; i++)
        {
            seqs[i].ShouldBeGreaterThan(seqs[i - 1]);
        }
    }

    [Fact]
    public async Task StartStream_two_tenants_get_independent_sequences_each_starting_at_1()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStream = NewStream();
        var betaStream = NewStream();

        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<StringTripSnapshot>(alphaStream, new StringTripStarted(alphaStream), new StringTripLeg(10));
            await s.SaveChangesAsync();
        }

        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<StringTripSnapshot>(betaStream, new StringTripStarted(betaStream), new StringTripLeg(20));
            await s.SaveChangesAsync();
        }

        // Each tenant's sequence is freshly minted on AddMartenManagedTenantsAsync —
        // so the first append for either tenant has seq_id = 1.
        await using var query = _fixture.Store.QuerySession(alpha);
        var alphaEvents = await query.Events.FetchStreamAsync(alphaStream);
        alphaEvents[0].Sequence.ShouldBe(1L);
        alphaEvents[1].Sequence.ShouldBe(2L);

        await using var queryB = _fixture.Store.QuerySession(beta);
        var betaEvents = await queryB.Events.FetchStreamAsync(betaStream);
        betaEvents[0].Sequence.ShouldBe(1L);
        betaEvents[1].Sequence.ShouldBe(2L);

        // The two tenants' seq_ids overlap (both have a seq_id == 1) — pinning
        // that cross-tenant seq_id is NOT a global ordering key under partitioning.
        alphaEvents.Select(e => e.Sequence).Intersect(betaEvents.Select(e => e.Sequence))
            .ShouldNotBeEmpty("per-tenant sequences are independent — cross-tenant seq_id overlap is correct, not a regression");
    }

    [Fact]
    public async Task StartStream_duplicate_id_within_tenant_collides()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = NewStream();
        await using (var first = _fixture.Store.LightweightSession(tenant))
        {
            first.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId));
            await first.SaveChangesAsync();
        }

        // Same key, same tenant — the second StartStream lands in the same
        // partition as the first. Under #4614, StartStream is routed through the
        // bulk mt_quick_append_events function with expected_version = 0 (the
        // StartStream contract — "this is a new stream"). The stream already
        // exists at version 1, so the function raises MT003, which TryTransform
        // surfaces as EventStreamUnexpectedMaxEventIdException — semantically a
        // ConcurrencyException about the "starting" version, not the
        // non-partitioned path's ExistingStreamIdCollisionException. Pin the
        // partitioned-path divergence so a future contract-unification gets
        // intentional review rather than accidental drift.
        await using (var second = _fixture.Store.LightweightSession(tenant))
        {
            second.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId));
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () => await second.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task same_stream_id_under_two_tenants_yields_independent_streams()
    {
        // The silent-split pin: same stream key, different tenants → two distinct
        // streams in two distinct partitions. NOT a collision.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var sharedStreamId = NewStream();

        await using (var sa = _fixture.Store.LightweightSession(alpha))
        {
            sa.Events.StartStream<StringTripSnapshot>(sharedStreamId, new StringTripStarted(sharedStreamId), new StringTripLeg(1));
            await sa.SaveChangesAsync();
        }

        await using (var sb = _fixture.Store.LightweightSession(beta))
        {
            sb.Events.StartStream<StringTripSnapshot>(sharedStreamId, new StringTripStarted(sharedStreamId), new StringTripLeg(2), new StringTripLeg(3));
            await sb.SaveChangesAsync();
        }

        // Each tenant queries and sees ONLY its own stream's events.
        await using var qa = _fixture.Store.QuerySession(alpha);
        var aEvents = await qa.Events.FetchStreamAsync(sharedStreamId);
        aEvents.Count.ShouldBe(2);

        await using var qb = _fixture.Store.QuerySession(beta);
        var bEvents = await qb.Events.FetchStreamAsync(sharedStreamId);
        bEvents.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Append_to_existing_stream_continues_version_and_per_tenant_seq()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId), new StringTripLeg(5));
            await s.SaveChangesAsync();
        }

        // Plain append (no version) — events get versions 3, 4.
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.Append(streamId, new StringTripLeg(10), new StringTripLeg(20));
            await s.SaveChangesAsync();
        }

        await using var query = _fixture.Store.QuerySession(tenant);
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(4);
        events.Select(e => e.Version).ShouldBe(new long[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task Append_to_archived_stream_throws_InvalidStreamOperationException()
    {
        // Archive raises MT001 server-side; TryTransform on the bulk operation
        // surfaces it as InvalidStreamOperationException client-side.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = NewStream();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<StringTripSnapshot>(streamId, new StringTripStarted(streamId));
            await s.SaveChangesAsync();
        }

        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.ArchiveStream(streamId);
            await s.SaveChangesAsync();
        }

        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.Append(streamId, new StringTripLeg(99));
            await Should.ThrowAsync<InvalidStreamOperationException>(async () => await s.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task multiple_streams_in_one_SaveChanges_each_land_in_the_tenants_partition()
    {
        // The bulk function processes one stream's events per call but multiple
        // streams' batches are queued in the same SaveChangesAsync. Pin that
        // they all land in the same tenant's partition + each stream's versions
        // start at 1.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamA = NewStream();
        var streamB = NewStream();
        var streamC = NewStream();

        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<StringTripSnapshot>(streamA, new StringTripStarted(streamA), new StringTripLeg(1));
            s.Events.StartStream<StringTripSnapshot>(streamB, new StringTripStarted(streamB));
            s.Events.StartStream<StringTripSnapshot>(streamC, new StringTripStarted(streamC), new StringTripLeg(2), new StringTripLeg(3));
            await s.SaveChangesAsync();
        }

        await using var query = _fixture.Store.QuerySession(tenant);
        (await query.Events.FetchStreamAsync(streamA)).Count.ShouldBe(2);
        (await query.Events.FetchStreamAsync(streamB)).Count.ShouldBe(1);
        (await query.Events.FetchStreamAsync(streamC)).Count.ShouldBe(3);
    }
}
