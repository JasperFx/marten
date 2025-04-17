using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Partitioning;

public class partitioning_tenant_removal : OneOffConfigurationsContext
{
    private string sanitizeGuid(Guid id) => id.ToString().Replace("-", "_");

    [Fact]
    public async Task tenant_removal_works_for_guid_tenant_id()
    {
        //arrange
        var tenantId = Guid.NewGuid();
        StoreOptions(o =>
        {
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            o.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
            o.Events.StreamIdentity = StreamIdentity.AsGuid;

            o.Schema.For<Company>().Identity(c => c.Id);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await theStore.Advanced.AddMartenManagedTenantsAsync(default,
            new Dictionary<string, string>() { { tenantId.ToString(), sanitizeGuid(tenantId) } });

        await using var session = theStore.LightweightSession(tenantId.ToString());
        session.Store(new Company
        {
            Id = Guid.NewGuid(),
            Name = "Test Company"
        });
        await session.SaveChangesAsync();

        //act
        await Should.NotThrowAsync(() => theStore.Advanced.DeleteAllTenantDataAsync(tenantId.ToString(), default));
    }

    [Fact]
    public async Task tenant_removal_works_for_sanitized_guid_tenant_id()
    {
        //arrange
        var tenantId = Guid.NewGuid();
        StoreOptions(o =>
        {
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            o.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
            o.Events.StreamIdentity = StreamIdentity.AsGuid;

            o.Schema.For<Company>().Identity(c => c.Id);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await theStore.Advanced.AddMartenManagedTenantsAsync(default,
            new Dictionary<string, string>() { { tenantId.ToString(), tenantId.ToString().Replace("-", "_") } });

        await using var session = theStore.LightweightSession(tenantId.ToString());
        session.Store(new Company
        {
            Id = Guid.NewGuid(),
            Name = "Test Company"
        });
        await session.SaveChangesAsync();

        //act
        await Should.NotThrowAsync(() => theStore.Advanced.DeleteAllTenantDataAsync(sanitizeGuid(tenantId), default));
    }
}
