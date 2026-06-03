using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_per_tenant_rebuild.cs
/// — the two loader-level configuration tests. These NEED OWN STORE because
/// they're exercising flag-state interactions (one builds without
/// <c>UseTenantPartitionedEvents</c>) and inspecting
/// <see cref="EventLoader.TenantFilterValue"/> directly off freshly-constructed
/// instances. They cannot share the fixture's pre-built store.
///
/// <para>
/// Schema names embed a Guid + ProcessId so net9 + net10 runs against the same
/// database never collide.
/// </para>
/// </summary>
public class event_loader_tenant_filter
{
    private static string UniqueSchema() =>
        $"tp_owned_{Guid.NewGuid().ToString("N")[..16]}_{Environment.ProcessId}";

    private static async Task ResetSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        // DROP SCHEMA … CASCADE wipes parent partitioned tables + per-tenant
        // partition children, so a re-run of the test starts from a clean
        // slate.
        try { await conn.DropSchemaAsync(schema); } catch (Exception) { }
    }

    private static DocumentStore BuildPartitionedStore(string schema)
    {
        return DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();

            o.Events.AddEventType<LocalTripStarted>();
            o.Events.AddEventType<LocalTripLeg>();
        });
    }

    [Fact]
    public async Task event_loader_applies_tenant_filter_only_when_shard_carries_tenant_AND_partitioned_events_is_on()
    {
        var schema = UniqueSchema() + "_on";
        await ResetSchemaAsync(schema);
        using var store = BuildPartitionedStore(schema);

        var db = (MartenDatabase)store.Storage.Database;

        // Off path: tenantless shard → no filter even with the flag on.
        var globalShard = ShardName.Compose("TripDistance");
        var loaderGlobal = new EventLoader(
            store, db, new AsyncOptions(), Array.Empty<ISqlFragment>(), globalShard);
        loaderGlobal.TenantFilterValue.ShouldBeNull(
            "tenantless shard → loader stays partition-agnostic even with UseTenantPartitionedEvents on");

        // On path: tenant-bearing shard + UseTenantPartitionedEvents on → loader scopes.
        var tenantShard = globalShard.ForTenant("alpha");
        var loaderTenant = new EventLoader(
            store, db, new AsyncOptions(), Array.Empty<ISqlFragment>(), tenantShard);
        loaderTenant.TenantFilterValue.ShouldBe("alpha",
            "per-tenant rebuild shard must surface its tenant slot to the loader so the SQL can partition-prune mt_events");
    }

    [Fact]
    public async Task event_loader_does_not_apply_tenant_filter_when_partitioned_events_is_off()
    {
        var schema = UniqueSchema() + "_unpartitioned";
        await ResetSchemaAsync(schema);
        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            // UseTenantPartitionedEvents intentionally OFF — even though the
            // shard carries a tenant slot, the loader must not invent the
            // filter because mt_events isn't partitioned and the literal
            // would gain nothing (and could miss the index).
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Events.AddEventType<LocalTripStarted>();
        });

        var db = (MartenDatabase)store.Storage.Database;
        var tenantShard = ShardName.Compose("TripDistance").ForTenant("alpha");

        var loader = new EventLoader(
            store, db, new AsyncOptions(), Array.Empty<ISqlFragment>(), tenantShard);
        loader.TenantFilterValue.ShouldBeNull(
            "UseTenantPartitionedEvents off → loader stays partition-agnostic even on a tenant shard");
    }

    // Local event types — keeping the file self-contained avoids any name
    // collisions with the fixture's TripStarted/TripLeg (these tests never
    // append; AddEventType just registers them for schema apply).
    public record LocalTripStarted(string Label);
    public record LocalTripLeg(int Miles);
}
