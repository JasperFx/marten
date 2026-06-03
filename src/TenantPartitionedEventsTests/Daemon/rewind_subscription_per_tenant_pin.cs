using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// #4617 section 4 deferred — pin that
/// <c>IProjectionDaemon.RewindSubscriptionAsync(name, tenantId, …)</c> throws
/// <see cref="NotSupportedException"/> when a non-null tenant id is supplied
/// against Marten's daemon — Marten deliberately did NOT override the
/// jasperfx default that throws on the per-tenant overload (per-tenant rewind
/// requires a daemon implementation that knows how to walk the per-tenant
/// progression rows back to a specific seq_id, which Marten hasn't built
/// yet). The tenant-less overload still works.
///
/// <para>
/// Pinned so a future per-tenant rewind implementation flips the assertion
/// intentionally rather than silently changing the contract.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class rewind_subscription_per_tenant_pin
{
    private readonly GuidPartitionedFixture _fixture;

    public rewind_subscription_per_tenant_pin(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task per_tenant_RewindSubscriptionAsync_throws_NotSupportedException()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        using var daemon = await _fixture.Store.BuildProjectionDaemonAsync();

        // The per-tenant overload with a non-null tenantId hits jasperfx's
        // default which throws NotSupportedException. Pin the throw so a
        // future Marten override that DOES support per-tenant rewind is a
        // deliberate contract change.
        var ex = await Should.ThrowAsync<NotSupportedException>(async () =>
            await daemon.RewindSubscriptionAsync(
                TripDistanceProjection.ProjectionName,
                tenantId: tenant,
                CancellationToken.None));

        // The thrown message points at the per-tenant capability gap.
        ex.Message.ShouldContain("per-tenant", Case.Insensitive);
    }

    [Fact]
    public async Task tenantless_RewindSubscriptionAsync_does_NOT_throw_under_partitioning()
    {
        // Null tenant id falls through to the store-global RewindSubscriptionAsync
        // path, which Marten DOES implement. Pin that this still works under
        // partitioning — only the per-tenant overload is gated.
        using var daemon = await _fixture.Store.BuildProjectionDaemonAsync();

        await Should.NotThrowAsync(async () =>
            await daemon.RewindSubscriptionAsync(
                TripDistanceProjection.ProjectionName,
                tenantId: null,
                CancellationToken.None,
                sequenceFloor: 0));
    }
}
