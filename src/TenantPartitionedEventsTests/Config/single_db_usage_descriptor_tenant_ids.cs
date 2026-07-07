using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// #4862 — a single-database store with <c>UseTenantPartitionedEvents</c> must surface its
/// Marten-managed tenants on the usage descriptor's <c>TenantIds</c>. Hosts that distribute
/// agents per identity (Wolverine-managed event subscription distribution) enumerate the
/// descriptor to fan agents out per tenant once <see cref="IEventStore.DistributesAgentsPerTenant"/>
/// is on — with an empty <c>TenantIds</c> the broadened gate would fan out to nothing.
/// DefaultTenancy leaves the list empty (only Master-Table/SingleServer/Static/Sharded tenancies
/// populate it) and stays completely untouched; instead the store bootstrap swaps in
/// <see cref="Marten.Storage.TenantPartitionedSingleDatabaseTenancy"/> for this permutation, whose
/// description reads the managed tenant-partition list — the same source
/// <c>ICrossTenantRebuildSource.FindRebuildTenantsAsync</c> uses.
/// </summary>
[Collection("guid-partitioned")]
public class single_db_usage_descriptor_tenant_ids
{
    private readonly GuidPartitionedFixture _fixture;

    public single_db_usage_descriptor_tenant_ids(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task usage_descriptor_carries_managed_tenants_in_tenant_ids()
    {
        var tenant1 = PartitionedFixtureBase.NewTenant();
        var tenant2 = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant1, tenant2);

        var usage = await ((IEventStore)_fixture.Store).TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
        usage.Database.Cardinality.ShouldBe(DatabaseCardinality.Single);
        usage.Database.MainDatabase.ShouldNotBeNull();

        // Fresh read from mt_tenant_partitions per description — tenants registered after the
        // store was built (and by other nodes) are visible without any cache invalidation.
        usage.Database.MainDatabase.TenantIds.ShouldContain(tenant1);
        usage.Database.MainDatabase.TenantIds.ShouldContain(tenant2);
    }
}
