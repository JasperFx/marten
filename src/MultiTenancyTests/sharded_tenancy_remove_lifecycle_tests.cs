using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4868 / #4880 — the tenant REMOVE side of the sharded-tenancy lifecycle on a RUNNING store.
/// Before the fix, <see cref="MartenDatabase.TenantIds"/> was add-only (`Fill` in
/// <c>BuildDatabases</c>/<c>AssignTenantAsync</c>) and <c>DescribeDatabasesAsync</c> copied that
/// in-memory list, so after <c>RemoveTenantAsync</c>/<c>DisableTenantAsync</c> a running store kept
/// reporting the tenant in its usage descriptor (<c>TryCreateUsage</c>) forever — hosts that retire
/// per-tenant daemon agents off the descriptor's supported-set diff (Wolverine-managed distribution)
/// could never see a tenant leave until a process restart.
///
/// Also pins the removal contract carved out in #4880:
/// <list type="bullet">
///   <item>REMOVE is destructive on the shard — partitions, per-tenant sequence, and per-tenant
///     progression rows are dropped/cleaned (mirrors the single-database
///     <c>RemoveMartenManagedTenantsAsync</c> / #4683 semantics); a re-added tenant starts fresh.</item>
///   <item>DISABLE is the non-destructive soft-delete — descriptor shrinks, all shard-side data
///     and registry rows are retained, ENABLE restores in place.</item>
/// </list>
/// </summary>
[Collection("sharded-tenancy")]
public class sharded_tenancy_remove_lifecycle_tests: IAsyncLifetime
{
    private readonly ShardedTenancyFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_tenancy_remove_lifecycle_tests(ShardedTenancyFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync("sharded"); } catch { }

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await ShardedTenancyFixture.cleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    private IDocumentStore CreateStore()
    {
        _store = buildStore();
        return _store;
    }

    private IDocumentStore buildStore()
    {
        return DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";
                foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
            });
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.RegisterDocumentType<Target>();
        });
    }

    private async Task applySchemaToAllShardsAsync()
    {
        var databases = await _store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }
    }

    private static IReadOnlyList<string> allTenantIds(DatabaseUsage usage)
        => usage.Databases.SelectMany(d => d.TenantIds).ToList();

    // ---- #4868: the usage descriptor must shrink on a RUNNING store ----

    [Fact]
    public async Task usage_descriptor_shrinks_after_RemoveTenantAsync_without_restart()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await _store.Advanced.AddTenantToShardAsync("rm_keep", CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("rm_gone", CancellationToken.None);

        var before = await sharded.DescribeDatabasesAsync(CancellationToken.None);
        allTenantIds(before).ShouldContain("rm_keep");
        allTenantIds(before).ShouldContain("rm_gone");

        await sharded.RemoveTenantAsync("rm_gone", CancellationToken.None);

        // SAME store instance, no restart — this is exactly what #4868 pinned as broken.
        var after = await sharded.DescribeDatabasesAsync(CancellationToken.None);
        allTenantIds(after).ShouldNotContain("rm_gone");
        allTenantIds(after).ShouldContain("rm_keep");
    }

    [Fact]
    public async Task usage_descriptor_shrinks_on_disable_and_regrows_on_enable()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var source = (IDynamicTenantSource<string>)sharded;

        var dbId = await source.AddTenantAsync("soft_tenant", CancellationToken.None);
        allTenantIds(await sharded.DescribeDatabasesAsync(CancellationToken.None))
            .ShouldContain("soft_tenant");

        await source.DisableTenantAsync("soft_tenant");
        allTenantIds(await sharded.DescribeDatabasesAsync(CancellationToken.None))
            .ShouldNotContain("soft_tenant");

        // DISABLE is non-destructive: partitions + shard registry rows are retained.
        (await shardRegistryContainsAsync(dbId, "soft_tenant")).ShouldBeTrue(
            "disable must NOT touch the shard's mt_tenant_partitions registry");
        (await shardHasPartitionTablesForAsync(dbId, "soft_tenant")).ShouldBeTrue(
            "disable must NOT drop the tenant's partitions");

        await source.EnableTenantAsync("soft_tenant");
        allTenantIds(await sharded.DescribeDatabasesAsync(CancellationToken.None))
            .ShouldContain("soft_tenant");
    }

    [Fact]
    public async Task descriptor_shrinks_when_tenant_removed_from_another_store_instance()
    {
        // The multi-node shape: node A keeps running while node B (another process/store)
        // removes the tenant. Node A's next DescribeDatabasesAsync — every distribution
        // cycle in a Wolverine-managed host — must re-enumerate fresh and see the tenant
        // gone. This is the "fresh reads should be the source for sharded tenancy too"
        // direction from #4868 (mirroring #4864's single-database rationale).
        CreateStore();
        await applySchemaToAllShardsAsync();
        var nodeA = (ShardedTenancy)_store.Options.Tenancy;

        await _store.Advanced.AddTenantToShardAsync("xnode_tenant", CancellationToken.None);

        // Node A materializes the tenant into its in-memory registry.
        allTenantIds(await nodeA.DescribeDatabasesAsync(CancellationToken.None))
            .ShouldContain("xnode_tenant");

        using (var storeB = buildStore())
        {
            var nodeB = (ShardedTenancy)storeB.Options.Tenancy;
            await nodeB.RemoveTenantAsync("xnode_tenant", CancellationToken.None);
        }

        allTenantIds(await nodeA.DescribeDatabasesAsync(CancellationToken.None))
            .ShouldNotContain("xnode_tenant");
    }

    [Fact]
    public async Task descriptor_follows_a_tenant_reassigned_to_another_shard()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await sharded.AssignTenantAsync("mover", _fixture.DbNames[0], CancellationToken.None);
        var before = await sharded.DescribeDatabasesAsync(CancellationToken.None);
        before.Databases.Single(d => d.Identifier == _fixture.DbNames[0]).TenantIds.ShouldContain("mover");

        await sharded.AssignTenantAsync("mover", _fixture.DbNames[1], CancellationToken.None);

        // The fresh-read reconciliation must move the tenant between shard descriptors,
        // not leave it reported on BOTH shards (the add-only `Fill` behavior).
        var after = await sharded.DescribeDatabasesAsync(CancellationToken.None);
        after.Databases.Single(d => d.Identifier == _fixture.DbNames[0]).TenantIds.ShouldNotContain("mover");
        after.Databases.Single(d => d.Identifier == _fixture.DbNames[1]).TenantIds.ShouldContain("mover");
    }

    [Fact]
    public async Task descriptor_reports_each_tenant_exactly_once()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await sharded.AssignTenantAsync("dupe_check", _fixture.DbNames[0], CancellationToken.None);
        // A second describe re-runs BuildDatabases' Fill pass — the descriptor snapshot
        // must still carry the tenant exactly once (Describe() + the explicit copy used
        // to AddRange-duplicate every tenant id).
        await sharded.DescribeDatabasesAsync(CancellationToken.None);
        var usage = await sharded.DescribeDatabasesAsync(CancellationToken.None);

        usage.Databases.Single(d => d.Identifier == _fixture.DbNames[0])
            .TenantIds.Count(t => t == "dupe_check").ShouldBe(1);
    }

    // ---- #4880: the destructive removal contract on the shard ----

    [Fact]
    public async Task remove_drops_shard_partitions_registry_rows_and_recomputes_tenant_count()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await sharded.AssignTenantAsync("hard_gone", _fixture.DbNames[0], CancellationToken.None);
        await sharded.AssignTenantAsync("hard_stays", _fixture.DbNames[0], CancellationToken.None);

        (await shardHasPartitionTablesForAsync(_fixture.DbNames[0], "hard_gone")).ShouldBeTrue();
        (await shardRegistryContainsAsync(_fixture.DbNames[0], "hard_gone")).ShouldBeTrue();

        await _store.Advanced.RemoveTenantFromShardAsync("hard_gone", CancellationToken.None);

        // Shard side: partitions + registry rows are gone (this is what lets the NATIVE
        // coordinator's per-tenant re-expansion — jasperfx#491 over ICrossTenantRebuildSource —
        // stop seeing the tenant and reap its agents), the surviving tenant is untouched.
        (await shardHasPartitionTablesForAsync(_fixture.DbNames[0], "hard_gone")).ShouldBeFalse(
            "removal must drop the tenant's list partitions from the shard");
        (await shardRegistryContainsAsync(_fixture.DbNames[0], "hard_gone")).ShouldBeFalse(
            "removal must remove the tenant from the shard's mt_tenant_partitions registry");
        (await shardHasPartitionTablesForAsync(_fixture.DbNames[0], "hard_stays")).ShouldBeTrue();
        (await shardRegistryContainsAsync(_fixture.DbNames[0], "hard_stays")).ShouldBeTrue();

        // Master side: assignment row deleted, tenant_count recomputed (#4763-style).
        (await sharded.FindDatabaseForTenantAsync("hard_gone", CancellationToken.None)).ShouldBeNull();
        var pool = await sharded.ListDatabasesAsync(CancellationToken.None);
        pool.First(d => d.DatabaseId == _fixture.DbNames[0]).TenantCount.ShouldBe(1);
    }

    [Fact]
    public async Task tenant_count_shrinks_on_disable_and_regrows_on_enable()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var source = (IDynamicTenantSource<string>)sharded;

        await sharded.AssignTenantAsync("count_soft", _fixture.DbNames[2], CancellationToken.None);
        (await countFor(sharded, _fixture.DbNames[2])).ShouldBe(1);

        // Disabled tenants are excluded from assignment ranking (AssignTenantAsync's recompute
        // filters on disabled = false) — Disable/Enable must keep tenant_count in step.
        await source.DisableTenantAsync("count_soft");
        (await countFor(sharded, _fixture.DbNames[2])).ShouldBe(0);

        await source.EnableTenantAsync("count_soft");
        (await countFor(sharded, _fixture.DbNames[2])).ShouldBe(1);
    }

    [Fact]
    public async Task remove_is_idempotent_and_a_removed_tenant_can_be_readded_fresh()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await sharded.AssignTenantAsync("recycled", _fixture.DbNames[1], CancellationToken.None);
        await sharded.RemoveTenantAsync("recycled", CancellationToken.None);

        // Re-runs (crash recovery, double-click in an admin UI) must no-op, not throw.
        await sharded.RemoveTenantAsync("recycled", CancellationToken.None);
        await sharded.RemoveTenantAsync("unknown_tenant_never_existed", CancellationToken.None);

        // Re-adding provisions the tenant from scratch — fresh partitions, resolvable again.
        var dbId = await _store.Advanced.AddTenantToShardAsync("recycled", CancellationToken.None);
        (await shardHasPartitionTablesForAsync(dbId, "recycled")).ShouldBeTrue();
        (await sharded.GetTenantAsync("recycled")).TenantId.ShouldBe("recycled");
        allTenantIds(await sharded.DescribeDatabasesAsync(CancellationToken.None))
            .ShouldContain("recycled");
    }

    [Fact]
    public async Task removing_a_disabled_tenant_still_cleans_its_shard()
    {
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var source = (IDynamicTenantSource<string>)sharded;

        await sharded.AssignTenantAsync("soft_then_hard", _fixture.DbNames[1], CancellationToken.None);
        await source.DisableTenantAsync("soft_then_hard");

        // The shard resolution is deliberately NOT filtered on the disabled flag here —
        // disable-then-remove is the natural retire flow and must not leak shard partitions.
        await sharded.RemoveTenantAsync("soft_then_hard", CancellationToken.None);

        (await shardHasPartitionTablesForAsync(_fixture.DbNames[1], "soft_then_hard")).ShouldBeFalse();
        (await shardRegistryContainsAsync(_fixture.DbNames[1], "soft_then_hard")).ShouldBeFalse();
        (await source.AllDisabledAsync()).ShouldNotContain("soft_then_hard");
    }

    [Fact]
    public async Task RemoveMartenManagedTenantsAsync_routes_to_the_sharded_removal_path()
    {
        // The store-agnostic admin surface (mirrors AddMartenManagedTenantsAsync's sharded
        // routing) — one call, full removal, suffix == tenant id under the sharded 1:1 shape.
        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await sharded.AssignTenantAsync("managed_gone", _fixture.DbNames[2], CancellationToken.None);

        await _store.Advanced.RemoveMartenManagedTenantsAsync(
            new[] { "managed_gone" }, CancellationToken.None);

        (await sharded.FindDatabaseForTenantAsync("managed_gone", CancellationToken.None)).ShouldBeNull();
        (await shardRegistryContainsAsync(_fixture.DbNames[2], "managed_gone")).ShouldBeFalse();
        allTenantIds(await sharded.DescribeDatabasesAsync(CancellationToken.None))
            .ShouldNotContain("managed_gone");
    }

    [Fact]
    public async Task remove_drops_the_sanitized_partition_for_a_hyphenated_tenant_id()
    {
        // weasel#338 regression, and the exact case the removed hand-rolled quoted DETACH/DROP
        // existed to cover: before Weasel 9.16 the native drop interpolated the partition name
        // unquoted and 42601'd on ids with hyphens/GUIDs. The native by-value drop now resolves
        // the SANITIZED suffix from the shard's own registry, so a hyphenated tenant's partition
        // (named with '-' → '_') is dropped and its raw-keyed registry row cleared — which is what
        // lets the coordinator's per-tenant re-expansion stop seeing the tenant and reap its agents.
        const string tenantId = "acme-corp-42";
        var sanitized = sanitizeSuffix(tenantId); // "acme_corp_42"
        sanitized.ShouldNotBe(tenantId, "the test id must actually require sanitization");

        CreateStore();
        await applySchemaToAllShardsAsync();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await sharded.AssignTenantAsync(tenantId, _fixture.DbNames[0], CancellationToken.None);

        // Create sanitizes the table name; the registry keys on the raw partition_value.
        (await shardHasPartitionTablesForSuffixAsync(_fixture.DbNames[0], sanitized)).ShouldBeTrue(
            "create should have made a partition named with the sanitized suffix");
        (await shardRegistryContainsAsync(_fixture.DbNames[0], tenantId)).ShouldBeTrue();

        await sharded.RemoveTenantAsync(tenantId, CancellationToken.None);

        (await shardHasPartitionTablesForSuffixAsync(_fixture.DbNames[0], sanitized)).ShouldBeFalse(
            "the native by-value drop must resolve + drop the sanitized partition for a hyphenated id");
        (await shardRegistryContainsAsync(_fixture.DbNames[0], tenantId)).ShouldBeFalse(
            "removal must clear the hyphenated tenant's raw-keyed registry row so the reap can proceed");
        (await sharded.FindDatabaseForTenantAsync(tenantId, CancellationToken.None)).ShouldBeNull();
    }

    // ---- helpers ----

    // Mirror of Weasel's internal ListPartition.SanitizeSuffix: lowercase, any char outside
    // [a-z0-9_] → '_'. Kept in the test because Weasel does not expose it publicly.
    private static string sanitizeSuffix(string suffix)
        => new string(suffix.ToLowerInvariant()
            .Select(c => (c is >= 'a' and <= 'z') || (c is >= '0' and <= '9') || c == '_' ? c : '_')
            .ToArray());

    private async Task<bool> shardHasPartitionTablesForSuffixAsync(string dbName, string suffix)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbName]);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync();
        return tables.Any(t => t.Name.EndsWith($"_{suffix}", StringComparison.Ordinal));
    }

    private static async Task<int> countFor(ShardedTenancy sharded, string dbName)
    {
        var pool = await sharded.ListDatabasesAsync(CancellationToken.None);
        return pool.First(d => d.DatabaseId == dbName).TenantCount;
    }

    private async Task<bool> shardHasPartitionTablesForAsync(string dbName, string tenantId)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbName]);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync();
        return tables.Any(t => t.Name.EndsWith($"_{tenantId}", StringComparison.Ordinal));
    }

    private async Task<bool> shardRegistryContainsAsync(string dbName, string tenantId)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbName]);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            "select count(*) from tenants.mt_tenant_partitions where partition_value = :id");
        cmd.Parameters.AddWithValue("id", tenantId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}
