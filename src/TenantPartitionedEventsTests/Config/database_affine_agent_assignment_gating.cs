using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// JasperFx/marten#4806 — the opt-in database-affine agent-assignment flag and how the
/// <see cref="IEventStore"/> member it drives is gated. <c>UseDatabaseAffineAgentAssignment</c> only
/// takes effect for a sharded per-tenant-partitioned store (the one tenancy where daemons fan agents out
/// per (shard, tenant)); everywhere else <c>GroupAgentAssignmentsByDatabase</c> stays false so distribution
/// behaves exactly as before. These are pure configuration assertions — building the store never opens a
/// connection.
/// </summary>
public class database_affine_agent_assignment_gating
{
    [Fact]
    public void use_database_affine_agent_assignment_defaults_to_false()
    {
        new StoreOptions().Events.UseDatabaseAffineAgentAssignment.ShouldBeFalse();
    }

    [Fact]
    public void group_by_database_is_off_on_a_single_database_store_even_with_the_flag_on()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.UseDatabaseAffineAgentAssignment = true;
        });

        var eventStore = (IEventStore)store;
        eventStore.GroupAgentAssignmentsByDatabase.ShouldBeFalse("affinity is only meaningful for a sharded store");
        eventStore.DistributesAgentsPerTenant.ShouldBeFalse("a single-database store keeps one agent per database");
    }

    [Fact]
    public void group_by_database_is_on_for_a_sharded_partitioned_store_with_the_flag_on()
    {
        using var store = ShardedStore(affine: true);

        var eventStore = (IEventStore)store;
        eventStore.DistributesAgentsPerTenant.ShouldBeTrue("sharded + per-tenant partitioning fans agents out per tenant");
        eventStore.GroupAgentAssignmentsByDatabase.ShouldBeTrue();
    }

    [Fact]
    public void group_by_database_stays_off_for_a_sharded_store_when_the_flag_is_off()
    {
        using var store = ShardedStore(affine: false);

        var eventStore = (IEventStore)store;
        eventStore.DistributesAgentsPerTenant.ShouldBeTrue("per-tenant fan-out does not depend on the affinity flag");
        eventStore.GroupAgentAssignmentsByDatabase.ShouldBeFalse("affinity must be explicitly opted into");
    }

    // A sharded, per-tenant-partitioned store with two databases. Dummy connection strings — the assertions
    // only read StoreOptions, so the store is never connected or provisioned.
    private static IDocumentStore ShardedStore(bool affine) =>
        DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "affine_gating";
                x.PartitionSchemaName = "affine_gating_tenants";
                x.AddDatabase("db1", ConnectionSource.ConnectionString);
                x.AddDatabase("db2", ConnectionSource.ConnectionString);
            });

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.UseDatabaseAffineAgentAssignment = affine;
        });
}
