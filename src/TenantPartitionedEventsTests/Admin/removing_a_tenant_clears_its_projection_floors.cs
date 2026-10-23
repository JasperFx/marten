using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// #5740 / jasperfx#809: under per-tenant partitioning a projection's start floor is stored per tenant, and a
/// re-added tenant restarts its sequence at 1. A floor its previous incarnation pinned would make the new one's
/// projections skip every event below it, so removing a tenant has to take its floors along. Other tenants'
/// floors and the store-global one stay.
/// </summary>
public class removing_a_tenant_clears_its_projection_floors: PartitionedStoreContext
{
    private const string Projection = "DelCountProjection";

    protected override string SchemaPrefix => "tp_floor";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<DelEvent>();
    }

    private IEventDatabase Database => (IEventDatabase)Store.Storage.Database;

    private async Task SeedFloorsAsync(string doomed, string kept)
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, doomed, kept);
        await SetFloorsAsync(doomed, kept);
    }

    private async Task SetFloorsAsync(string doomed, string kept)
    {
        await Database.SetProjectionFloorAsync(Projection, doomed, 100);
        await Database.SetProjectionFloorAsync(Projection, kept, 200);
        await Database.SetProjectionFloorAsync(Projection, null, 300);
    }

    private async Task ShouldHaveOnlyTheSurvivorsAsync(string doomed, string kept)
    {
        (await Database.FetchProjectionFloorAsync(Projection, doomed)).ShouldBeNull();
        (await Database.FetchProjectionFloorAsync(Projection, kept)).ShouldBe(200);
        (await Database.FetchProjectionFloorAsync(Projection, null)).ShouldBe(300);
    }

    [Fact]
    public async Task DeleteAllTenantDataAsync_removes_the_tenants_floors()
    {
        await SeedFloorsAsync("floor_del", "floor_del_kept");

        await Store.Advanced.DeleteAllTenantDataAsync("floor_del", CancellationToken.None);

        await ShouldHaveOnlyTheSurvivorsAsync("floor_del", "floor_del_kept");
    }

    [Fact]
    public async Task RemoveMartenManagedTenantsAsync_removes_the_tenants_floors()
    {
        await SeedFloorsAsync("floor_rem", "floor_rem_kept");

        await Store.Advanced.RemoveMartenManagedTenantsAsync(["floor_rem"], CancellationToken.None);

        await ShouldHaveOnlyTheSurvivorsAsync("floor_rem", "floor_rem_kept");
    }

    [Fact]
    public async Task a_guid_tenant_named_by_its_partition_suffix_loses_its_floors()
    {
        // Floors are keyed by the canonical tenant id, while RemoveMartenManagedTenantsAsync is handed the
        // hyphen-free partition suffix
        var doomed = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, doomed, kept);
        await SetFloorsAsync(doomed.ToString(), kept.ToString());

        await Store.Advanced.RemoveMartenManagedTenantsAsync([doomed.ToString("N")], CancellationToken.None);

        await ShouldHaveOnlyTheSurvivorsAsync(doomed.ToString(), kept.ToString());
    }
}
