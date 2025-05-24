using JasperFx.Descriptors;
using JasperFx.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class jasperfx_event_store_mechanics : OneOffConfigurationsContext
{
    public IEventStore theEventStore => theStore;

    [Fact]
    public void cardinality_and_tenancy_by_default()
    {
        theEventStore.DatabaseCardinality.ShouldBe(DatabaseCardinality.Single);
        theEventStore.HasMultipleTenants.ShouldBeFalse();
    }

    [Fact]
    public void tenancy_for_event_store_conjoined()
    {
        StoreOptions(opts => opts.Events.TenancyStyle = TenancyStyle.Conjoined);

        theEventStore.DatabaseCardinality.ShouldBe(DatabaseCardinality.Single);
        theEventStore.HasMultipleTenants.ShouldBeTrue();
    }

    [Fact]
    public void tenancy_for_multiple_databases()
    {
        StoreOptions(opts =>
        {
            opts.MultiTenantedDatabases(x => x.AddSingleTenantDatabase(ConnectionSource.ConnectionString, "tenant1"));
        });

        theEventStore.DatabaseCardinality.ShouldBe(DatabaseCardinality.StaticMultiple);
        theEventStore.HasMultipleTenants.ShouldBeTrue();
    }
}
