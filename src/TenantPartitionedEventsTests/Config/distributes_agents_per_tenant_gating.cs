using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// wolverine#3280 + #4862 — how <see cref="IEventStore.DistributesAgentsPerTenant"/> is gated. It turns
/// on for ANY per-tenant-partitioned store: independent, overlapping mt_events_sequence_&lt;tenant&gt;
/// sequences co-located in one events table mean a store-global agent has no correct progression
/// semantics, whether that table lives in a single conjoined database or a shard (#4862 showed the
/// single-DB case silently skipping a lagging tenant's events under Wolverine-managed distribution).
/// Without partitioning it stays false so distribution behaves exactly as before: one agent per
/// database. These are pure configuration assertions — building the store never opens a connection.
/// </summary>
public class distributes_agents_per_tenant_gating
{
    [Fact]
    public void on_for_a_single_database_store_with_per_tenant_partitioning()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
        });

        // #4862: flipped from false. Per-tenant sequences overlap in the single events table
        // exactly as co-located tenants do in a shard, so per-identity agent starts must fan
        // out per tenant here too.
        ((IEventStore)store).DistributesAgentsPerTenant
            .ShouldBeTrue("single-database per-tenant partitioning still has overlapping per-tenant sequences");
    }

    [Fact]
    public void off_by_default_on_a_plain_single_database_store()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
        });

        ((IEventStore)store).DistributesAgentsPerTenant
            .ShouldBeFalse("without per-tenant partitioning there is a single store-global sequence");
    }

    [Fact]
    public void on_for_a_sharded_per_tenant_partitioned_store()
    {
        using var store = ShardedStore(partitioned: true);

        ((IEventStore)store).DistributesAgentsPerTenant
            .ShouldBeTrue("sharded + per-tenant partitioning fans agents out per tenant");
    }

    [Fact]
    public void off_for_a_sharded_store_without_per_tenant_partitioning()
    {
        using var store = ShardedStore(partitioned: false);

        ((IEventStore)store).DistributesAgentsPerTenant
            .ShouldBeFalse("without per-tenant partitioning a shard database has a single sequence");
    }

    // A sharded store with two databases. Dummy connection strings — the assertions only read
    // StoreOptions, so the store is never connected or provisioned.
    private static IDocumentStore ShardedStore(bool partitioned) =>
        DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "distributes_gating";
                x.PartitionSchemaName = "distributes_gating_tenants";
                x.AddDatabase("db1", ConnectionSource.ConnectionString);
                x.AddDatabase("db2", ConnectionSource.ConnectionString);
            });

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = partitioned;
        });
}
