using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4607 — soft-delete (Disable / Enable / AllDisabled) lifecycle on
/// <see cref="ShardedTenancy"/>'s <see cref="IDynamicTenantSource{T}"/> implementation.
/// Mirrors <see cref="MasterTableTenancy"/>'s soft-delete semantics so the two
/// dynamic sources behave uniformly behind the store-agnostic
/// <c>DynamicTenancyAdminExtensions</c>. Backed by a Marten-added
/// <c>disabled boolean not null default false</c> column on the existing
/// <c>mt_tenant_assignments</c> table (<see cref="MartenTenantAssignmentTable"/>).
/// </summary>
[Collection("sharded-tenancy")]
public class sharded_tenancy_soft_delete_tests : IAsyncLifetime
{
    private readonly ShardedTenancyFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_tenancy_soft_delete_tests(ShardedTenancyFixture fixture)
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
        _store = DocumentStore.For(opts =>
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
        return _store;
    }

    // ---- DisableTenantAsync ----

    [Fact]
    public async Task disabled_tenant_resolution_throws_UnknownTenantIdException()
    {
        CreateStore();
        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;

        await source.AddTenantAsync("tenant-a", CancellationToken.None);
        // Sanity: tenant is resolvable before the soft-delete.
        (await _store.Options.Tenancy.GetTenantAsync("tenant-a")).TenantId.ShouldBe("tenant-a");

        await source.DisableTenantAsync("tenant-a");

        await Should.ThrowAsync<UnknownTenantIdException>(async () =>
            await _store.Options.Tenancy.GetTenantAsync("tenant-a"));
    }

    [Fact]
    public async Task disabled_tenant_is_filtered_from_FindDatabaseForTenantAsync()
    {
        CreateStore();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var source = (IDynamicTenantSource<string>)sharded;

        var dbId = await source.AddTenantAsync("tenant-b", CancellationToken.None);
        (await sharded.FindDatabaseForTenantAsync("tenant-b", CancellationToken.None)).ShouldBe(dbId);

        await source.DisableTenantAsync("tenant-b");

        (await sharded.FindDatabaseForTenantAsync("tenant-b", CancellationToken.None))
            .ShouldBeNull("FindDatabaseForTenantAsync must hide soft-deleted assignments");
    }

    [Fact]
    public async Task auto_assign_on_a_disabled_tenant_throws_UnknownTenantIdException_not_resurrects()
    {
        // The dangerous case: without the under-lock disabled-check, auto-assign would
        // create a fresh assignment for a disabled tenant — silently undoing the
        // soft-delete and possibly placing the tenant on a different shard than where
        // its data lives.
        CreateStore();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var source = (IDynamicTenantSource<string>)sharded;

        var originalDbId = await source.AddTenantAsync("tenant-c", CancellationToken.None);
        await source.DisableTenantAsync("tenant-c");

        await Should.ThrowAsync<UnknownTenantIdException>(async () =>
            await sharded.GetTenantAsync("tenant-c"));

        // No reassignment happened — the disabled row still points at the original shard.
        var rawAssignment = await readRawAssignment(_store, "tenant-c");
        rawAssignment.databaseId.ShouldBe(originalDbId, "the disabled row must keep its original database_id");
        rawAssignment.disabled.ShouldBeTrue("the disabled flag must still be set");
    }

    [Fact]
    public async Task DisableTenantAsync_is_idempotent_for_already_disabled_or_unknown()
    {
        CreateStore();
        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;

        await source.AddTenantAsync("tenant-d", CancellationToken.None);
        await source.DisableTenantAsync("tenant-d");
        await source.DisableTenantAsync("tenant-d"); // already disabled — no-op
        await source.DisableTenantAsync("unknown-tenant"); // never existed — no-op
    }

    // ---- EnableTenantAsync ----

    [Fact]
    public async Task EnableTenantAsync_restores_resolution_for_a_previously_disabled_tenant()
    {
        CreateStore();
        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;

        var dbId = await source.AddTenantAsync("tenant-e", CancellationToken.None);
        await source.DisableTenantAsync("tenant-e");

        // Pre-condition: disabled
        await Should.ThrowAsync<UnknownTenantIdException>(async () =>
            await _store.Options.Tenancy.GetTenantAsync("tenant-e"));

        await source.EnableTenantAsync("tenant-e");

        var tenant = await _store.Options.Tenancy.GetTenantAsync("tenant-e");
        tenant.TenantId.ShouldBe("tenant-e");

        // Same shard the tenant had before the soft-delete — re-enable doesn't relocate.
        (await ((ShardedTenancy)_store.Options.Tenancy).FindDatabaseForTenantAsync("tenant-e", CancellationToken.None))
            .ShouldBe(dbId);
    }

    [Fact]
    public async Task EnableTenantAsync_is_idempotent_for_already_enabled_or_unknown()
    {
        CreateStore();
        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;

        await source.AddTenantAsync("tenant-f", CancellationToken.None);
        await source.EnableTenantAsync("tenant-f"); // already enabled
        await source.EnableTenantAsync("unknown-tenant"); // never existed
    }

    // ---- AllDisabledAsync ----

    [Fact]
    public async Task AllDisabledAsync_returns_only_currently_disabled_tenants()
    {
        CreateStore();
        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;

        await source.AddTenantAsync("active-1", CancellationToken.None);
        await source.AddTenantAsync("disabled-1", CancellationToken.None);
        await source.AddTenantAsync("disabled-2", CancellationToken.None);
        await source.AddTenantAsync("active-2", CancellationToken.None);

        await source.DisableTenantAsync("disabled-1");
        await source.DisableTenantAsync("disabled-2");

        var disabled = await source.AllDisabledAsync();

        disabled.OrderBy(x => x).ShouldBe(new[] { "disabled-1", "disabled-2" });
    }

    [Fact]
    public async Task AllDisabledAsync_is_empty_when_no_tenants_are_disabled()
    {
        CreateStore();
        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;

        await source.AddTenantAsync("active-only", CancellationToken.None);

        (await source.AllDisabledAsync()).ShouldBeEmpty();
    }

    // ---- Re-enable via explicit assignment ----

    [Fact]
    public async Task explicit_assign_via_AddTenantAsync_with_databaseId_reactivates_a_disabled_tenant()
    {
        CreateStore();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var source = (IDynamicTenantSource<string>)sharded;

        await source.AddTenantAsync("tenant-g", CancellationToken.None);
        await source.DisableTenantAsync("tenant-g");

        // Caller-supplied overload of AddTenantAsync — semantically "assign tenant
        // to this specific database". Stronger intent than DisableTenantAsync, so
        // the explicit re-assignment clears the disabled flag.
        await source.AddTenantAsync("tenant-g", _fixture.DbNames[0]);

        (await source.AllDisabledAsync()).ShouldNotContain("tenant-g");
        (await sharded.FindDatabaseForTenantAsync("tenant-g", CancellationToken.None))
            .ShouldBe(_fixture.DbNames[0]);
    }

    // ---- Cross-source admin extension surface (jasperfx#413) ----

    [Fact]
    public async Task lifecycle_works_through_the_store_agnostic_dynamic_tenant_source_interface()
    {
        // This is the surface CritterWatch's tenant-management UI uses — it resolves
        // IDynamicTenantSource<string> from DI and calls the lifecycle methods without
        // sniffing the concrete tenancy type. Sharded must behave like MasterTable.
        CreateStore();
        IDynamicTenantSource<string> source = (IDynamicTenantSource<string>)_store.Options.Tenancy;

        await source.AddTenantAsync("uniform-tenant", CancellationToken.None);
        await source.DisableTenantAsync("uniform-tenant");
        (await source.AllDisabledAsync()).ShouldContain("uniform-tenant");

        await source.EnableTenantAsync("uniform-tenant");
        (await source.AllDisabledAsync()).ShouldNotContain("uniform-tenant");

        await source.RemoveTenantAsync("uniform-tenant");
        (await source.AllDisabledAsync()).ShouldNotContain("uniform-tenant");
    }

    // ---- helpers ----

    private static async Task<(string databaseId, bool disabled)> readRawAssignment(IDocumentStore store, string tenantId)
    {
        // Direct read against the assignment table to verify the on-disk state
        // independent of the caching paths the production code uses.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var schemaName = "sharded";
        var tableName = TenantAssignmentTable.TableName;
        var disabledColumn = MartenTenantAssignmentTable.DisabledColumn;
        await using var reader = await ((System.Data.Common.DbCommand)conn
                .CreateCommand(
                    $"select database_id, {disabledColumn} from {schemaName}.{tableName} where tenant_id = :id")
                .With("id", tenantId))
            .ExecuteReaderAsync();

        if (!await reader.ReadAsync()) throw new InvalidOperationException($"No row for tenant '{tenantId}'");
        var dbId = await reader.GetFieldValueAsync<string>(0);
        var disabled = await reader.GetFieldValueAsync<bool>(1);
        await reader.CloseAsync();
        return (dbId, disabled);
    }
}
