#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

public class Doc4863A
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Doc4863B
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public record Evt4863(string Name);

/// <summary>
/// Own collection + own schema names (NOT the shared "sharded-tenant-partitioned"
/// fixture's public/tenants/sharded schemas) so these tests never race the other
/// sharded tests — or sibling test runs — over shared schema state. The physical
/// shard databases are reused; everything these tests create lives in the
/// bug4863* schemas that only this class touches.
/// </summary>
[CollectionDefinition("per-db-partition-state", DisableParallelization = true)]
public sealed class PerDbPartitionStateCollection: ICollectionFixture<PerDbPartitionStateFixture>;

public sealed class PerDbPartitionStateFixture: IAsyncLifetime
{
    public const string ShardA = "marten_shard_a";
    public const string ShardB = "marten_shard_b";

    public Dictionary<string, string> ConnectionStrings { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { ShardA, ShardB })
        {
            if (!await conn.DatabaseExists(name))
            {
                await new DatabaseSpecification().BuildDatabase(conn, name);
            }

            ConnectionStrings[name] = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
            {
                Database = name
            }.ConnectionString;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// #4863 + #4855 — the per-database partition-state family for sharded, Marten-managed
/// tenant partitioning.
///
/// <para>
/// #4863: a projection/document table created LAZILY by a fresh store instance (a node that
/// joins after tenants were provisioned on that shard) must hydrate its per-tenant LIST
/// partitions from the shard's own <c>mt_tenant_partitions</c> registry. Before the fix the
/// lazy table-creation path consulted only the store-wide in-memory
/// <c>ManagedListPartitions</c> snapshot (empty on a fresh node, because sharded tenancy
/// never registered the registry initializer on its shard databases), so the table was
/// created partitioned-by-tenant with ZERO partitions and every write failed with 23514.
/// </para>
///
/// <para>
/// #4855: every shard database materialized partitions for every registered tenant of the
/// whole store — the store-shared partition dictionary fed each database's delta, so the
/// first provisioning touch of a fresh shard created partitions (and, on full applies,
/// per-tenant event sequences) for all foreign tenants too. Quadratic at scale. The
/// per-database expected set must come from that database's own registry.
/// </para>
/// </summary>
[Collection("per-db-partition-state")]
public class Bug_4863_4855_per_database_partition_state: IAsyncLifetime
{
    private const string DocSchema = "bug4863";
    private const string MasterSchema = "bug4863_master";
    private const string TenantSchema = "bug4863_tenants";

    private readonly PerDbPartitionStateFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Bug_4863_4855_per_database_partition_state(PerDbPartitionStateFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(MasterSchema); } catch { }

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var shardConn = new NpgsqlConnection(connStr);
            await shardConn.OpenAsync();
            try { await shardConn.DropSchemaAsync(DocSchema); } catch { }
            try { await shardConn.DropSchemaAsync(TenantSchema); } catch { }

            // The additive tenant-provisioning path (AddTenantToShardAsync →
            // AddPartitionToAllTables → Table.MigrateAsync) creates tables but not
            // schemas, so pre-create the non-public schemas the way a full apply would.
            await shardConn.CreateCommand($"create schema if not exists {DocSchema}").ExecuteNonQueryAsync();
            await shardConn.CreateCommand($"create schema if not exists {TenantSchema}").ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore BuildStore(Action<StoreOptions>? extra = null) => (DocumentStore)DocumentStore.For(opts =>
    {
        opts.MultiTenantedWithShardedDatabases(x =>
        {
            x.ConnectionString = ConnectionSource.ConnectionString;
            x.SchemaName = MasterSchema;
            x.PartitionSchemaName = TenantSchema;
            foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
            {
                x.AddDatabase(dbName, connStr);
            }
        });

        opts.DatabaseSchemaName = DocSchema;
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.DisableNpgsqlLogging = true;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        // Activate the event store feature so mt_events / mt_streams / the per-tenant
        // sequences participate in schema applies and the additive provisioning path.
        opts.Events.AddEventType(typeof(Evt4863));
        opts.Projections.DaemonLockId = 48635;

        opts.Schema.For<Doc4863A>().DocumentAlias("bug4863_a");

        extra?.Invoke(opts);
    });

    [Fact] // #4863
    public async Task lazily_created_doc_table_on_a_fresh_node_hydrates_partitions_from_the_shards_own_registry()
    {
        // Node 1 provisions two tenants onto shard A and writes through the known doc type,
        // so shard A's mt_tenant_partitions registry holds both tenants and the existing
        // tables have their partitions.
        using (var node1 = BuildStore())
        {
            await node1.Advanced.AddTenantToShardAsync("t4863_one", PerDbPartitionStateFixture.ShardA, CancellationToken.None);
            await node1.Advanced.AddTenantToShardAsync("t4863_two", PerDbPartitionStateFixture.ShardA, CancellationToken.None);

            await using var seed = node1.LightweightSession("t4863_one");
            seed.Store(new Doc4863A { Id = Guid.NewGuid(), Name = "seeded" });
            await seed.SaveChangesAsync();
        }

        // Node 2 is a FRESH store instance that additionally registers Doc4863B, whose
        // table does not exist on the shard yet. The tenant is already assigned, so no
        // provisioning path runs — the table is created lazily by the first write.
        using var node2 = BuildStore(opts => opts.Schema.For<Doc4863B>().DocumentAlias("bug4863_b"));

        await using var session = node2.LightweightSession("t4863_one");
        session.Store(new Doc4863B { Id = Guid.NewGuid(), Name = "written by fresh node" });

        // Before the fix: mt_doc_bug4863_b is created partitioned-by-tenant with ZERO
        // partitions and this fails with 23514 "no partition of relation found for row".
        await session.SaveChangesAsync();

        // And the lazily-created table must hydrate partitions for EVERY tenant resident
        // on the shard (from the shard's own registry), not just the writing tenant.
        var partitions = await ListPartitions(PerDbPartitionStateFixture.ShardA, DocSchema, "mt_doc_bug4863_b");
        partitions.ShouldContain("mt_doc_bug4863_b_t4863_one");
        partitions.ShouldContain("mt_doc_bug4863_b_t4863_two");
    }

    [Fact] // #4855
    public async Task a_shard_database_only_materializes_partitions_for_its_own_tenants()
    {
        using var store = BuildStore();

        // Two tenants land on shard A first (provision + write, the real usage flow), so
        // the store-wide in-memory snapshot holds them by the time shard B is first touched.
        foreach (var tenant in new[] { "t4855_a1", "t4855_a2" })
        {
            await store.Advanced.AddTenantToShardAsync(tenant, PerDbPartitionStateFixture.ShardA, CancellationToken.None);
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new Evt4863(tenant));
            session.Store(new Doc4863A { Id = Guid.NewGuid(), Name = tenant });
            await session.SaveChangesAsync();
        }

        // First touch of shard B — before the fix the lazily-created tables on shard B
        // materialized partitions for t4855_a1/t4855_a2 as well (the store-shared
        // expected set), i.e. the quadratic cross-product of #4855.
        await store.Advanced.AddTenantToShardAsync("t4855_b1", PerDbPartitionStateFixture.ShardB, CancellationToken.None);
        await using (var sessionB = store.LightweightSession("t4855_b1"))
        {
            sessionB.Events.StartStream(Guid.NewGuid(), new Evt4863("t4855_b1"));
            sessionB.Store(new Doc4863A { Id = Guid.NewGuid(), Name = "t4855_b1" });
            await sessionB.SaveChangesAsync();
        }

        var shardBStreams = await ListPartitions(PerDbPartitionStateFixture.ShardB, DocSchema, "mt_streams");
        shardBStreams.ShouldBe(new[] { "mt_streams_t4855_b1" }, ignoreOrder: true,
            customMessage: "shard B must only ever materialize partitions for tenants resident on shard B");

        var shardBDocs = await ListPartitions(PerDbPartitionStateFixture.ShardB, DocSchema, "mt_doc_bug4863_a");
        shardBDocs.ShouldBe(new[] { "mt_doc_bug4863_a_t4855_b1" }, ignoreOrder: true);

        // Shard A keeps exactly its own two tenants.
        var shardAStreams = await ListPartitions(PerDbPartitionStateFixture.ShardA, DocSchema, "mt_streams");
        shardAStreams.ShouldBe(new[] { "mt_streams_t4855_a1", "mt_streams_t4855_a2" }, ignoreOrder: true);

        // A full pod-startup style apply must not leak foreign tenants into shard B
        // either — neither table partitions nor per-tenant event sequences.
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await ListPartitions(PerDbPartitionStateFixture.ShardB, DocSchema, "mt_streams"))
            .ShouldBe(new[] { "mt_streams_t4855_b1" }, ignoreOrder: true,
                customMessage: "a full apply re-materialized foreign tenant partitions on shard B");

        var shardBSequences = await ListPerTenantSequences(PerDbPartitionStateFixture.ShardB, DocSchema);
        shardBSequences.ShouldBe(new[] { "mt_events_sequence_t4855_b1" }, ignoreOrder: true,
            customMessage: "a full apply created per-tenant event sequences on shard B for tenants that live on shard A");

        // Sanity: writes work per shard after all of the above.
        await using (var sa = store.LightweightSession("t4855_a1"))
        {
            sa.Events.StartStream(Guid.NewGuid(), new Evt4863("a1"));
            sa.Store(new Doc4863A { Id = Guid.NewGuid(), Name = "a1" });
            await sa.SaveChangesAsync();
        }

        await using (var sb = store.LightweightSession("t4855_b1"))
        {
            sb.Events.StartStream(Guid.NewGuid(), new Evt4863("b1"));
            sb.Store(new Doc4863A { Id = Guid.NewGuid(), Name = "b1" });
            await sb.SaveChangesAsync();
        }
    }

    [Fact] // #4855 — fresh shard joins an established store
    public async Task full_apply_on_a_fresh_shard_does_not_create_partitions_for_foreign_tenants()
    {
        using var store = BuildStore();

        await store.Advanced.AddTenantToShardAsync("t4855_c1", PerDbPartitionStateFixture.ShardA, CancellationToken.None);
        await store.Advanced.AddTenantToShardAsync("t4855_c2", PerDbPartitionStateFixture.ShardA, CancellationToken.None);

        // Shard B has no tenants. A full apply (pod start) must create its parent tables
        // EMPTY of tenant partitions — not with shard A's tenants.
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await ListPartitions(PerDbPartitionStateFixture.ShardB, DocSchema, "mt_streams"))
            .ShouldBeEmpty("a fresh shard database materialized partitions for tenants assigned to another shard");

        (await ListPerTenantSequences(PerDbPartitionStateFixture.ShardB, DocSchema))
            .ShouldBeEmpty();

        // And shard A got exactly its own.
        (await ListPartitions(PerDbPartitionStateFixture.ShardA, DocSchema, "mt_streams"))
            .ShouldBe(new[] { "mt_streams_t4855_c1", "mt_streams_t4855_c2" }, ignoreOrder: true);
    }

    private async Task<IReadOnlyList<string>> ListPartitions(string shard, string schema, string table)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[shard]);
        await conn.OpenAsync();

        var list = new List<string>();
        var cmd = conn.CreateCommand(
            "select c.relname from pg_inherits i " +
            "join pg_class c on c.oid = i.inhrelid " +
            "join pg_class p on p.oid = i.inhparent " +
            "join pg_namespace n on n.oid = p.relnamespace " +
            "where n.nspname = :schema and p.relname = :table");
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(await reader.GetFieldValueAsync<string>(0));
        }

        return list;
    }

    private async Task<IReadOnlyList<string>> ListPerTenantSequences(string shard, string schema)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[shard]);
        await conn.OpenAsync();

        var list = new List<string>();
        var cmd = conn.CreateCommand(
            "select sequencename from pg_sequences where schemaname = :schema " +
            "and sequencename like 'mt\\_events\\_sequence\\_%' escape '\\'");
        cmd.Parameters.AddWithValue("schema", schema);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(await reader.GetFieldValueAsync<string>(0));
        }

        return list;
    }
}
