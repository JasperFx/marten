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
using Xunit;

namespace MultiTenancyTests;

/// <summary>
///     #5986: the synchronous <c>ShardedTenancy.GetTenant</c> skips the #4942 first-sight repair only when the tenant
///     is already fully provisioned. Each case damages one thing the repair would put back, behind the store's back,
///     and checks that a synchronous resolution in a fresh store still puts it back.
/// </summary>
[Collection(TenantResolutionThreadPoolCollection.Name)]
public class sharded_sync_tenant_resolution_tests
{
    private const string Damaged = "heal_a";
    private const string Healthy = "heal_b";

    [Theory]
    [InlineData("dropped document partition")]
    [InlineData("detached document partition")]
    [InlineData("document partition bound without the tenant")]
    [InlineData("missing registry row")]
    [InlineData("different registry suffix")]
    [InlineData("registry primary key dropped")]
    [InlineData("unregistered partitioned table without the partition")]
    public async Task sync_resolution_still_repairs_a_tenant_that_is_missing_something(string damage)
    {
        await using var shards = await ScratchShards.CreateAsync(2);

        try
        {
            var dbId = await shards.ProvisionAsync([Damaged, Healthy]);
            await damageAsync(shards, dbId[Damaged], damage);

            // A fresh store is a process that has not seen either tenant
            await using var store = shards.BuildStore();
            await shards.RunOneOffWorkAsync(store);

            store.Options.Tenancy.GetTenant(Damaged).Database.Identifier.ShouldBe(dbId[Damaged]);

            await assertRepairedAsync(shards, dbId[Damaged], damage);

            // And the tenant can write to its partition again, synchronously
            using (var session = store.LightweightSession(Damaged))
            {
                session.Store(new Target { Id = Guid.NewGuid() });
                await session.SaveChangesAsync();
            }

            // The untouched tenant resolves synchronously too
            using (var session = store.LightweightSession(Healthy))
            {
                session.Store(new Target { Id = Guid.NewGuid() });
                await session.SaveChangesAsync();
            }
        }
        finally
        {
            await shards.DropMasterSchemaAsync();
        }
    }

    [Fact]
    public async Task sync_resolution_of_a_provisioned_tenant_skips_the_repair()
    {
        await using var shards = await ScratchShards.CreateAsync(2);

        try
        {
            var dbId = (await shards.ProvisionAsync([Healthy]))[Healthy];
            var partitionBefore = await shards.PartitionStateAsync(dbId, Healthy);

            // The repair always upserts the tenant's registry row, which gives the row a new version. Whether the
            // row version moved is what tells a skipped repair from one that ran and found nothing to create
            var rowVersion = await shards.RegistryRowVersionAsync(dbId, Healthy);

            await using (var store = shards.BuildStore())
            {
                await shards.RunOneOffWorkAsync(store);

                var database = (MartenDatabase)store.Options.Tenancy.GetTenant(Healthy).Database;
                database.Identifier.ShouldBe(dbId);

                // The in-memory state the repair would have left: this shard's partition view, read from its
                // registry, holds the tenant
                store.Options.TenantPartitions!.Partitions.PartitionsFor(database)
                    .ShouldContainKeyAndValue(Healthy, Healthy);

                using (var session = store.LightweightSession(Healthy))
                {
                    session.Store(new Target { Id = Guid.NewGuid() });
                    await session.SaveChangesAsync();
                }
            }

            (await shards.RegistryRowVersionAsync(dbId, Healthy)).ShouldBe(rowVersion);
            (await shards.PartitionStateAsync(dbId, Healthy)).ShouldBe(partitionBefore);

            // The asynchronous path is unchanged and still runs the repair on first sight, which moves the row version
            await using (var store = shards.BuildStore())
            {
                await store.Options.Tenancy.GetTenantAsync(Healthy);
            }

            (await shards.RegistryRowVersionAsync(dbId, Healthy)).ShouldNotBe(rowVersion);
        }
        finally
        {
            await shards.DropMasterSchemaAsync();
        }
    }

    [Fact]
    public async Task sync_resolution_of_new_and_disabled_tenants_is_unchanged()
    {
        await using var shards = await ScratchShards.CreateAsync(2);

        try
        {
            await shards.ProvisionAsync(["disabled_a"]);

            await using (var admin = shards.BuildStore())
            {
                await ((ShardedTenancy)admin.Options.Tenancy).DisableTenantAsync("disabled_a");
            }

            await using var store = shards.BuildStore();
            await shards.RunOneOffWorkAsync(store);

            // A brand-new tenant still goes through the assignment strategy and gets provisioned
            var assigned = store.Options.Tenancy.GetTenant("brand_new").Database.Identifier;
            (await shards.PartitionStateAsync(assigned, "brand_new")).ShouldBe("attached: FOR VALUES IN ('brand_new')");

            using (var session = store.LightweightSession("brand_new"))
            {
                session.Store(new Target { Id = Guid.NewGuid() });
                await session.SaveChangesAsync();
            }

            // And a disabled tenant is still refused the same way
            Should.Throw<DisabledTenantException>(() => store.Options.Tenancy.GetTenant("disabled_a"));
        }
        finally
        {
            await shards.DropMasterSchemaAsync();
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task sync_resolution_works_when_the_control_or_shard_databases_are_multiplexed(
        bool controlMultiplexing, bool shardMultiplexing)
    {
        await using var shards = await ScratchShards.CreateAsync(2, controlMultiplexing, shardMultiplexing);

        try
        {
            var dbId = await shards.ProvisionAsync([Damaged, Healthy]);
            await damageAsync(shards, dbId[Damaged], "dropped document partition");

            await using var store = shards.BuildStore();
            await shards.RunOneOffWorkAsync(store);

            // Npgsql refuses synchronous I/O on a multiplexed data source, so whichever lookup would need it keeps
            // the asynchronous path instead of throwing NotSupportedException
            store.Options.Tenancy.GetTenant(Healthy).Database.Identifier.ShouldBe(dbId[Healthy]);
            store.Options.Tenancy.GetTenant(Damaged).Database.Identifier.ShouldBe(dbId[Damaged]);

            await assertRepairedAsync(shards, dbId[Damaged], "dropped document partition");

            // And an unknown-to-this-store tenant is still assigned
            store.Options.Tenancy.GetTenant("brand_new").TenantId.ShouldBe("brand_new");
        }
        finally
        {
            await shards.DropMasterSchemaAsync();
        }
    }

    private static async Task damageAsync(ScratchShards shards, string dbId, string damage)
    {
        var sql = damage switch
        {
            "dropped document partition" => $"drop table public.mt_doc_target_{Damaged}",
            "detached document partition" =>
                $"alter table public.mt_doc_target detach partition public.mt_doc_target_{Damaged}",
            "document partition bound without the tenant" =>
                $"alter table public.mt_doc_target detach partition public.mt_doc_target_{Damaged}; " +
                $"alter table public.mt_doc_target attach partition public.mt_doc_target_{Damaged} for values in ('heal_zz')",
            "missing registry row" => $"delete from tenants.mt_tenant_partitions where partition_value = '{Damaged}'",
            "different registry suffix" =>
                $"update tenants.mt_tenant_partitions set partition_suffix = upper(partition_suffix) where partition_value = '{Damaged}'",
            // The registry table itself needs a migration, which the repair runs before its upsert
            "registry primary key dropped" =>
                "alter table tenants.mt_tenant_partitions drop constraint pkey_mt_tenant_partitions_partition_value",
            // A tenant-partitioned table this store does not register, as another application or a newer
            // deployment might have created. The #4944 sweep gives it the tenant's partition too
            "unregistered partitioned table without the partition" =>
                "create table public.mt_doc_unregistered (id uuid not null, tenant_id varchar not null, data jsonb, primary key (tenant_id, id)) partition by list (tenant_id)",
            _ => throw new ArgumentOutOfRangeException(nameof(damage))
        };

        await shards.ExecuteAsync(dbId, sql);
    }

    private static async Task assertRepairedAsync(ScratchShards shards, string dbId, string damage)
    {
        switch (damage)
        {
            case "dropped document partition":
            case "detached document partition":
                (await shards.PartitionStateAsync(dbId, Damaged)).ShouldBe($"attached: FOR VALUES IN ('{Damaged}')");
                break;

            case "document partition bound without the tenant":
                // The repair widens the bound rather than replacing it
                var state = await shards.PartitionStateAsync(dbId, Damaged);
                state.ShouldStartWith("attached: ");
                state.ShouldContain($"'{Damaged}'");
                state.ShouldContain("'heal_zz'");
                break;

            case "missing registry row":
            case "different registry suffix":
                (await shards.ScalarAsync(dbId,
                        $"select partition_suffix from tenants.mt_tenant_partitions where partition_value = '{Damaged}'"))
                    .ShouldBe(Damaged);
                break;

            case "registry primary key dropped":
                (await shards.ScalarAsync(dbId,
                        "select string_agg(a.attname, ',') from pg_index x join pg_attribute a on a.attrelid = x.indrelid and a.attnum = any(x.indkey) where x.indrelid = 'tenants.mt_tenant_partitions'::regclass and x.indisprimary"))
                    .ShouldBe("partition_value");
                break;

            case "unregistered partitioned table without the partition":
                (await shards.ScalarAsync(dbId,
                        $"select pg_get_expr(relpartbound, oid) from pg_class where relname = 'mt_doc_unregistered_{Damaged}'"))
                    .ShouldBe($"FOR VALUES IN ('{Damaged}')");
                break;
        }
    }
}

/// <summary>
///     Shard databases of a sharded tenancy, created for one test and dropped after it, with a control schema of their
///     own in the test database. Nothing is shared with any other test or test run.
/// </summary>
internal sealed class ScratchShards: IAsyncDisposable
{
    private readonly string[] _databaseNames;
    private readonly bool _controlMultiplexing;

    private ScratchShards(string masterSchema, string[] databaseNames, bool controlMultiplexing,
        bool shardMultiplexing)
    {
        MasterSchema = masterSchema;
        _databaseNames = databaseNames;
        _controlMultiplexing = controlMultiplexing;

        ConnectionStrings = databaseNames.ToDictionary(x => x, x => new NpgsqlConnectionStringBuilder(
            ConnectionSource.ConnectionString) { Database = x, Multiplexing = shardMultiplexing }.ConnectionString);
    }

    public string MasterSchema { get; }

    public IReadOnlyDictionary<string, string> ConnectionStrings { get; }

    public static async Task<ScratchShards> CreateAsync(int count, bool controlMultiplexing = false,
        bool shardMultiplexing = false)
    {
        var prefix = "m5986_" + Guid.NewGuid().ToString("N")[..8];
        var names = Enumerable.Range(0, count).Select(i => $"{prefix}_{(char)('a' + i)}").ToArray();

        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();

        foreach (var name in names)
        {
            // template0 has no connections, so concurrent creates elsewhere on the server cannot block this one
            await conn.CreateCommand($"create database {name} template template0").ExecuteNonQueryAsync();
        }

        return new ScratchShards(prefix + "_pool", names, controlMultiplexing, shardMultiplexing);
    }

    public DocumentStore BuildStore()
    {
        return DocumentStore.For(opts =>
        {
            opts.DisableNpgsqlLogging = true;
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
                {
                    Multiplexing = _controlMultiplexing
                }.ConnectionString;
                x.SchemaName = MasterSchema;

                foreach (var (dbId, connectionString) in ConnectionStrings)
                {
                    x.AddDatabase(dbId, connectionString);
                }
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.RegisterDocumentType<Target>();
        });
    }

    /// <summary>
    ///     Builds every shard's schema and provisions the tenants from a store of their own, which is then disposed:
    ///     a warm database for whatever store the test builds next. Returns each tenant's shard.
    /// </summary>
    public async Task<Dictionary<string, string>> ProvisionAsync(string[] tenantIds)
    {
        await using var store = BuildStore();

        foreach (var database in (await store.Options.Tenancy.BuildDatabases()).OfType<IMartenDatabase>())
        {
            await database.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        var assignments = new Dictionary<string, string>();
        foreach (var tenantId in tenantIds)
        {
            assignments[tenantId] = await store.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        }

        return assignments;
    }

    /// <summary>
    ///     The store's one-off work before its first tenant resolution, the pool schema check and the seeding of the
    ///     configured databases, done asynchronously so that no tenant is cached by it.
    /// </summary>
    public async Task RunOneOffWorkAsync(IDocumentStore store)
    {
        await ((ShardedTenancy)store.Options.Tenancy).ListDatabasesAsync(CancellationToken.None);
    }

    /// <summary>
    ///     "attached: {bound}", "detached" or "missing" for the tenant's <c>mt_doc_target</c> partition.
    /// </summary>
    public async Task<string> PartitionStateAsync(string dbId, string tenantId)
    {
        await using var conn = new NpgsqlConnection(plain(dbId));
        await conn.OpenAsync();

        await using var reader = await conn
            .CreateCommand("select pg_get_expr(relpartbound, oid) from pg_class where relname = :name")
            .With("name", $"mt_doc_target_{tenantId}")
            .ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return "missing";
        return await reader.IsDBNullAsync(0) ? "detached" : "attached: " + reader.GetString(0);
    }

    public async Task<string?> RegistryRowVersionAsync(string dbId, string tenantId)
    {
        await using var conn = new NpgsqlConnection(plain(dbId));
        await conn.OpenAsync();
        return await conn
            .CreateCommand("select xmin::text from tenants.mt_tenant_partitions where partition_value = :id")
            .With("id", tenantId)
            .ExecuteScalarAsync() as string;
    }

    public async Task ExecuteAsync(string dbId, string sql)
    {
        await using var conn = new NpgsqlConnection(plain(dbId));
        await conn.OpenAsync();
        await conn.CreateCommand(sql).ExecuteNonQueryAsync();
    }

    public async Task<string?> ScalarAsync(string dbId, string sql)
    {
        await using var conn = new NpgsqlConnection(plain(dbId));
        await conn.OpenAsync();
        return await conn.CreateCommand(sql).ExecuteScalarAsync() as string;
    }

    public async Task DropMasterSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.CreateCommand($"drop schema if exists {MasterSchema} cascade").ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();

        foreach (var name in _databaseNames)
        {
            await conn.CreateCommand($"drop database if exists {name} with (force)").ExecuteNonQueryAsync();
        }
    }

    // The test's own reads and writes never multiplex, whatever the store does
    private string plain(string dbId)
        => new NpgsqlConnectionStringBuilder(ConnectionStrings[dbId]) { Multiplexing = false }.ConnectionString;
}
