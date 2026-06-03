using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.OptimisticConcurrency;

/// <summary>
/// #4614 — optimistic-concurrency event appends now work under
/// <c>UseTenantPartitionedEvents</c>. Before this fix, every
/// <c>FetchForWriting</c> / <c>AppendOptimistic</c> / <c>AppendExclusive</c> /
/// expected-version <c>StartStream</c>+<c>Append</c> shape threw
/// <c>NotSupportedException</c> at <c>SaveChangesAsync</c> from
/// <c>QuickEventAppender.registerOperationsForStreams</c>, blocking the
/// canonical CQRS aggregate-handler write pattern (Wolverine
/// <c>[AggregateHandler]</c>) on every partitioned store.
///
/// <para>
/// The implementation routes through the bulk <c>mt_quick_append_events</c>
/// function (the only path under partitioning), which now accepts a trailing
/// <c>expected_version</c> parameter, checks it against the stream's actual
/// version, and raises SQLSTATE MT003 on mismatch.
/// <c>QuickAppendEventsOperationBase.TryTransform</c> translates MT003 back to
/// <see cref="EventStreamUnexpectedMaxEventIdException"/> so the user-facing
/// exception matches the rich path's <c>UpdateStreamVersion</c> contract.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class Optimistic_concurrency_under_partitioned_events_guid
{
    private readonly GuidPartitionedFixture _fixture;

    public Optimistic_concurrency_under_partitioned_events_guid(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FetchForWriting_then_append_succeeds_on_existing_stream()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        // Seed the stream — Quick path, no version on this call.
        var streamId = Guid.NewGuid();
        await using (var seed = _fixture.Store.LightweightSession(tenant))
        {
            seed.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId), new TripLeg(1));
            await seed.SaveChangesAsync();
        }

        // The canonical aggregate-handler shape: load, decide, append.
        // Pre-#4614, the SaveChangesAsync below threw NotSupportedException —
        // this assertion is the headline regression-guard.
        await using var session = _fixture.Store.LightweightSession(tenant);
        var stream = await session.Events.FetchForWriting<TripSnapshot>(streamId);
        stream.AppendOne(new TripLeg(2));
        stream.AppendOne(new TripLeg(3));
        await session.SaveChangesAsync();

        await using var query = _fixture.Store.QuerySession(tenant);
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(4); // TripStarted + 3 TripLegs
    }

    [Fact]
    public async Task FetchForWriting_on_brand_new_stream_then_append_succeeds()
    {
        // FetchForWriting against a never-started stream sets ExpectedVersionOnServer = 0
        // (the surprising-but-correct behavior #4614's repro called out). Under
        // partitioning that previously tripped the throw because 0.HasValue is true.
        // It now routes through the bulk function with expected_version = 0, which
        // COALESCEs the NULL stream-version to 0 and matches.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using var session = _fixture.Store.LightweightSession(tenant);
        var stream = await session.Events.FetchForWriting<TripSnapshot>(streamId);
        stream.AppendOne(new TripStarted(streamId));
        stream.AppendOne(new TripLeg(5));
        await session.SaveChangesAsync();

        await using var query = _fixture.Store.QuerySession(tenant);
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task FetchForWriting_with_stale_expected_version_throws_concurrency_exception()
    {
        // The version-check teeth: two sessions race the same stream. The second
        // SaveChangesAsync must see the first's append and throw the rich path's
        // standard exception type (EventStreamUnexpectedMaxEventIdException, which
        // inherits ConcurrencyException) — pin the EXACT exception type so the
        // bulk path's MT003 translation stays equivalent to the rich path's
        // UpdateStreamVersion behavior.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var seed = _fixture.Store.LightweightSession(tenant))
        {
            seed.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
            await seed.SaveChangesAsync();
        }

        // Session A loads — sees version 1.
        await using var sessionA = _fixture.Store.LightweightSession(tenant);
        var streamA = await sessionA.Events.FetchForWriting<TripSnapshot>(streamId);

        // Session B writes a competing event behind A's back — bumps the stream
        // to version 2.
        await using (var sessionB = _fixture.Store.LightweightSession(tenant))
        {
            var streamB = await sessionB.Events.FetchForWriting<TripSnapshot>(streamId);
            streamB.AppendOne(new TripLeg(10));
            await sessionB.SaveChangesAsync();
        }

        // Now A tries to commit — its ExpectedVersionOnServer is 1, but the
        // stream is at 2. MT003 from the SQL function, translated to
        // EventStreamUnexpectedMaxEventIdException by TryTransform.
        streamA.AppendOne(new TripLeg(99));
        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
            await sessionA.SaveChangesAsync());
    }

    [Fact]
    public async Task AppendOptimistic_with_correct_expected_version_succeeds()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var seed = _fixture.Store.LightweightSession(tenant))
        {
            seed.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId), new TripLeg(1));
            await seed.SaveChangesAsync();
        }

        await using var session = _fixture.Store.LightweightSession(tenant);
        await session.Events.AppendOptimistic(streamId, new TripLeg(2));
        await session.SaveChangesAsync();

        await using var query = _fixture.Store.QuerySession(tenant);
        (await query.Events.FetchStreamAsync(streamId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task AppendOptimistic_on_non_existent_stream_throws_NonExistentStreamException()
    {
        // AppendOptimistic eagerly reads the stream's current state to compute
        // ExpectedVersionOnServer. A non-existent stream returns null, so this
        // throws NonExistentStreamException DURING THE APPEND CALL — same as the
        // non-partitioned path, never reaches SaveChangesAsync.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using var session = _fixture.Store.LightweightSession(tenant);
        await Should.ThrowAsync<NonExistentStreamException>(async () =>
            await session.Events.AppendOptimistic(streamId, new TripLeg(1)));
    }

    [Fact]
    public async Task AppendExclusive_then_append_succeeds_and_releases_lock()
    {
        // AppendExclusive acquires an advisory lock on the stream id then routes
        // through the same optimistic path under partitioning. Most importantly,
        // verify the session disposes cleanly so the connection isn't poisoned —
        // pre-#4614 the throw at SaveChangesAsync left a leaked aborted tx in
        // some shapes.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var seed = _fixture.Store.LightweightSession(tenant))
        {
            seed.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
            await seed.SaveChangesAsync();
        }

        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            await session.Events.AppendExclusive(streamId, new TripLeg(7));
            await session.SaveChangesAsync();
        }

        // A follow-up plain append on the same tenant proves the prior session
        // released its lock + disposed cleanly.
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.Append(streamId, new TripLeg(8));
            await session.SaveChangesAsync();
        }

        await using var query = _fixture.Store.QuerySession(tenant);
        (await query.Events.FetchStreamAsync(streamId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task expected_version_check_is_tenant_isolated()
    {
        // Two tenants race the same stream id. Each tenant's stream is a separate
        // row in mt_streams (different partitions), so an append in tenant A
        // must NOT trip a version-mismatch in tenant B's session — the version
        // check is scoped to (tenant_id, stream_id), not stream_id alone.
        var tenantA = PartitionedFixtureBase.NewTenant();
        var tenantB = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(
            CancellationToken.None, tenantA, tenantB);

        var streamId = Guid.NewGuid();
        await using (var seedA = _fixture.Store.LightweightSession(tenantA))
        {
            seedA.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
            await seedA.SaveChangesAsync();
        }
        await using (var seedB = _fixture.Store.LightweightSession(tenantB))
        {
            seedB.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
            await seedB.SaveChangesAsync();
        }

        // Each tenant's FetchForWriting reads its own stream — both at version 1.
        // Both append concurrently — both succeed.
        await using var sessionA = _fixture.Store.LightweightSession(tenantA);
        var streamA = await sessionA.Events.FetchForWriting<TripSnapshot>(streamId);
        await using var sessionB = _fixture.Store.LightweightSession(tenantB);
        var streamB = await sessionB.Events.FetchForWriting<TripSnapshot>(streamId);

        streamA.AppendOne(new TripLeg(1));
        streamB.AppendOne(new TripLeg(2));

        await sessionA.SaveChangesAsync();
        await sessionB.SaveChangesAsync(); // would throw if version check leaked across tenants
    }
}
