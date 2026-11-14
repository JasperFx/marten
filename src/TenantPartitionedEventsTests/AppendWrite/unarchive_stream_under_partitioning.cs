using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.AppendWrite;

/// <summary>
/// marten#5476 — <c>UnArchiveStream</c> under <c>UseTenantPartitionedEvents</c>. That layout requires conjoined
/// tenancy and refuses <c>UseArchivedStreamPartitioning</c>, so <c>mt_unarchive_stream</c> takes the
/// flag-flipping path scoped by <c>tenant_id</c>. Un-archiving one tenant's stream must leave another tenant's
/// archived stream with the same id archived, and the reopened stream must accept the quick append the
/// layout is restricted to (which is where the MT001 archived check lives).
/// </summary>
[Collection("guid-partitioned")]
public class unarchive_stream_under_partitioning
{
    private readonly GuidPartitionedFixture _fixture;

    public unarchive_stream_under_partitioning(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task unarchive_in_one_tenant_leaves_the_same_id_stream_in_another_tenant_archived()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var sharedId = Guid.NewGuid();
        foreach (var tenant in new[] { alpha, beta })
        {
            await using var s = _fixture.Store.LightweightSession(tenant);
            s.Events.StartStream<TripSnapshot>(sharedId, new TripStarted(sharedId), new TripLeg(1));
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);

            s.Events.ArchiveStream(sharedId);
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.UnArchiveStream(sharedId);
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var qa = _fixture.Store.QuerySession(alpha))
        {
            var state = await qa.Events.FetchStreamStateAsync(sharedId, TestContext.Current.CancellationToken);
            state.ShouldNotBeNull();
            state.IsArchived.ShouldBeFalse();
            state.Version.ShouldBe(2);

            (await qa.Events.FetchStreamAsync(sharedId, token: TestContext.Current.CancellationToken))
                .Count.ShouldBe(2);
        }

        await using (var qb = _fixture.Store.QuerySession(beta))
        {
            var state = await qb.Events.FetchStreamStateAsync(sharedId, TestContext.Current.CancellationToken);
            state.ShouldNotBeNull();
            state.IsArchived.ShouldBeTrue("beta's same-id stream was not un-archived");

            (await qb.Events.FetchStreamAsync(sharedId, token: TestContext.Current.CancellationToken))
                .ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task an_unarchived_stream_accepts_appends_again()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);

            s.Events.ArchiveStream(streamId);
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);

            s.Events.UnArchiveStream(streamId);
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Through the quick-append function's MT001 check, and at the expected version
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.Append(streamId, 2, new TripLeg(5));
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            var stream = await s.Events.FetchForWriting<TripSnapshot>(streamId, TestContext.Current.CancellationToken);
            stream.Aggregate.ShouldNotBeNull();
            stream.AppendOne(new TripLeg(7));
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = _fixture.Store.QuerySession(tenant);
        var state = await query.Events.FetchStreamStateAsync(streamId, TestContext.Current.CancellationToken);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(3);
        state.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task unarchiving_a_stream_that_does_not_exist_creates_nothing()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.UnArchiveStream(streamId);
            await s.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = _fixture.Store.QuerySession(tenant);
        (await query.Events.FetchStreamStateAsync(streamId, TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
