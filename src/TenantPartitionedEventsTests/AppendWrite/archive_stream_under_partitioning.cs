using System;
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
/// #4617 section 3e — <c>ArchiveStream</c> under <c>UseTenantPartitionedEvents</c>.
/// The <c>mt_archive_stream</c> SQL function scopes its UPDATE / INSERT / DELETE
/// by <c>tenant_id</c> when <see cref="JasperFx.MultiTenancy.TenancyStyle.Conjoined"/>
/// is on, so archiving one tenant's stream must not touch another tenant's
/// stream — even when both tenants share the exact same stream id (the
/// silent-split shape established by the AppendWrite tests).
///
/// <para>
/// After archive: the owning tenant's <c>FetchStreamStateAsync.IsArchived</c>
/// reads <c>true</c>; the other tenant's same-id stream stays
/// <c>IsArchived = false</c>; and any re-append to the archived stream throws
/// <see cref="InvalidStreamOperationException"/> (MT001).
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class archive_stream_under_partitioning
{
    private readonly GuidPartitionedFixture _fixture;

    public archive_stream_under_partitioning(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task archive_in_one_tenant_does_not_archive_same_id_stream_in_other_tenant()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Both tenants start a stream with THE SAME id (independent streams in
        // independent partitions per the silent-split pin).
        var sharedId = Guid.NewGuid();
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.StartStream<TripSnapshot>(sharedId, new TripStarted(sharedId), new TripLeg(1));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(beta))
        {
            s.Events.StartStream<TripSnapshot>(sharedId, new TripStarted(sharedId), new TripLeg(2));
            await s.SaveChangesAsync();
        }

        // Archive only alpha's stream.
        await using (var s = _fixture.Store.LightweightSession(alpha))
        {
            s.Events.ArchiveStream(sharedId);
            await s.SaveChangesAsync();
        }

        // Alpha's stream state shows archived; beta's same-id stream stays live.
        await using (var qa = _fixture.Store.QuerySession(alpha))
        {
            var alphaState = await qa.Events.FetchStreamStateAsync(sharedId);
            alphaState.ShouldNotBeNull();
            alphaState!.IsArchived.ShouldBeTrue(
                "alpha's stream was archived — its mt_streams row must have is_archived = true");
        }

        await using (var qb = _fixture.Store.QuerySession(beta))
        {
            var betaState = await qb.Events.FetchStreamStateAsync(sharedId);
            betaState.ShouldNotBeNull(
                "beta's same-id stream must survive — archive is scoped by (tenant_id, stream_id)");
            betaState!.IsArchived.ShouldBeFalse(
                "beta's stream was NOT archived — its is_archived stays false despite the shared stream id");
        }
    }

    [Fact]
    public async Task re_append_to_archived_stream_throws_InvalidStreamOperationException()
    {
        // The archive function flips is_archived on mt_streams; subsequent
        // appends hit the MT001 guard in mt_quick_append_events and surface as
        // InvalidStreamOperationException via TryTransform.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
            await s.SaveChangesAsync();
        }

        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.ArchiveStream(streamId);
            await s.SaveChangesAsync();
        }

        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.Append(streamId, new TripLeg(99));
            await Should.ThrowAsync<InvalidStreamOperationException>(async () =>
                await s.SaveChangesAsync());
        }
    }
}
