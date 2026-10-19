using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Coordination;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// <see cref="ShardedTenancy.FindOrCreateDatabase"/> takes a tenant id <em>or a database id</em>, and
/// <c>IProjectionCoordinator.DaemonForDatabase</c> hands it a shard's database id. Both caches it consults
/// before falling through to auto-assignment are filled lazily: <c>_tenantToDatabase</c> by tenant
/// resolution, <c>_databasesById</c> only by <c>AddDatabaseAsync</c> and <c>BuildDatabases</c>. So on a store
/// that has not read its pool yet — every store right after start, until the coordinator's first
/// distribution pass — a shard's database id missed both, fell through to
/// <c>findOrAssignTenantDatabaseAsync</c>, and was registered as a <em>tenant</em> named after the shard,
/// on whichever shard the assignment strategy picked, with partition DDL for it on that shard.
/// </summary>
/// <remarks>
/// Each test runs against a freshly built store that has not called <c>BuildDatabases</c>, which is what
/// makes the cold cache deterministic here; behind a started host the same window is a race against the
/// coordinator's leadership loop.
/// </remarks>
[Collection("sharded-tenancy")]
public class sharded_database_id_lookup_does_not_provision: IAsyncLifetime
{
    private const string ShardDatabaseId = "marten_shard_a";

    private readonly ShardedTenancyFixture _fixture;
    private DocumentStore _store = null!;

    public sharded_database_id_lookup_does_not_provision(ShardedTenancyFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        try { await conn.DropSchemaAsync("sharded", TestContext.Current.CancellationToken); } catch { }

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync(TestContext.Current.CancellationToken);
            try { await tenantConn.DropSchemaAsync("tenants", TestContext.Current.CancellationToken); } catch { }
            await ShardedTenancyFixture.cleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private void CreateStore(bool seedPool = true)
    {
        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";

                if (!seedPool) return;

                foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Projections.AsyncMode = DaemonMode.Solo;
            opts.RegisterDocumentType<Target>();
        });
    }

    [Fact]
    public async Task a_shard_database_id_resolves_to_that_shard_before_the_pool_is_read()
    {
        CreateStore();

        var database = await _store.Storage.FindOrCreateDatabase(ShardDatabaseId);

        // The side effect first, so a regression reports what it wrote rather than only where it landed.
        (await assignmentRowCountAsync(ShardDatabaseId)).ShouldBe(0,
            "resolving a database id must not register a tenant named after it");
        (await shardsWithPartitionForAsync(ShardDatabaseId)).ShouldBeEmpty(
            "resolving a database id must not run tenant partition DDL on any shard");
        database.Identifier.ShouldBe(ShardDatabaseId);
    }

    [Fact]
    public async Task daemon_for_a_shard_database_id_runs_against_that_shard_and_registers_no_tenant()
    {
        CreateStore();
        await using var coordinator = new ProjectionCoordinator(_store, NullLogger<ProjectionCoordinator>.Instance);

        var daemon = await coordinator.DaemonForDatabase(ShardDatabaseId);

        (await assignmentRowCountAsync(ShardDatabaseId)).ShouldBe(0,
            "looking up a shard's daemon must not register a tenant named after the shard");
        (await shardsWithPartitionForAsync(ShardDatabaseId)).ShouldBeEmpty(
            "looking up a shard's daemon must not run tenant partition DDL on any shard");
        ((ProjectionDaemon)daemon).Database.Identifier.ShouldBe(ShardDatabaseId);
    }

    [Fact]
    public async Task a_shard_database_id_resolves_on_a_store_that_did_not_seed_the_pool()
    {
        // The pool rows are written by one store; a second store that configures no databases of its own,
        // as on a node that only reads a pool another node manages, has nothing seeded into its cache.
        CreateStore();
        await _store.Options.Tenancy.BuildDatabases();
        var seeder = _store;

        CreateStore(seedPool: false);
        try
        {
            var database = await _store.Storage.FindOrCreateDatabase(ShardDatabaseId);

            (await assignmentRowCountAsync(ShardDatabaseId)).ShouldBe(0,
                "resolving a database id must not register a tenant named after it");
            database.Identifier.ShouldBe(ShardDatabaseId);
        }
        finally
        {
            seeder.Dispose();
        }
    }

    [Fact]
    public async Task an_unknown_tenant_id_is_still_assigned_to_a_shard()
    {
        // The other half of the contract: "find or create" still creates for a genuine tenant id.
        CreateStore();
        const string tenantId = "never_seen_tenant";

        var database = await _store.Storage.FindOrCreateDatabase(tenantId);

        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        (await sharded.FindDatabaseForTenantAsync(tenantId, TestContext.Current.CancellationToken))
            .ShouldBe(database.Identifier);
        (await shardsWithPartitionForAsync(tenantId)).ShouldBe(new[] { database.Identifier });
    }

    [Fact]
    public async Task an_assigned_tenant_still_resolves_to_its_own_shard_on_a_cold_store()
    {
        CreateStore();
        const string tenantId = "pre_assigned_tenant";
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        await sharded.AssignTenantAsync(tenantId, "marten_shard_b", TestContext.Current.CancellationToken);

        // A second store over the same pool has neither cache filled.
        var assigner = _store;
        CreateStore();
        try
        {
            var database = await _store.Storage.FindOrCreateDatabase(tenantId);
            database.Identifier.ShouldBe("marten_shard_b");
        }
        finally
        {
            assigner.Dispose();
        }
    }

    private static async Task<long> assignmentRowCountAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        // Every row, disabled or not: the claim is that nothing was written.
        await using var cmd = conn.CreateCommand(
            $"select count(*) from sharded.{TenantAssignmentTable.TableName} where tenant_id = @id");
        cmd.Parameters.AddWithValue("id", tenantId);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<string[]> shardsWithPartitionForAsync(string tenantId)
    {
        var shards = new System.Collections.Generic.List<string>();
        foreach (var (dbName, connStr) in _fixture.ConnectionStrings.OrderBy(x => x.Key))
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(TestContext.Current.CancellationToken);

            var exists = await conn.CreateCommand("select to_regclass('tenants.mt_tenant_partitions') is not null")
                .ExecuteScalarAsync(TestContext.Current.CancellationToken);
            if (exists is not true) continue;

            await using var cmd = conn.CreateCommand(
                "select count(*) from tenants.mt_tenant_partitions where partition_value = @id");
            cmd.Parameters.AddWithValue("id", tenantId);
            var count = (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
            if (count > 0) shards.Add(dbName);
        }

        return shards.ToArray();
    }
}
