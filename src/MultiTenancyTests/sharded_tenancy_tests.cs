using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using JasperFx.MultiTenancy;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace MultiTenancyTests;

[CollectionDefinition("sharded-tenancy", DisableParallelization = true)]
public class ShardedTenancyCollection : ICollectionFixture<ShardedTenancyFixture>;

public class ShardedTenancyFixture : IAsyncLifetime
{
    public string[] DbNames { get; private set; } = null!;
    public Dictionary<string, string> ConnectionStrings { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        DbNames = new[] { "marten_shard_a", "marten_shard_b", "marten_shard_c" };
        ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in DbNames)
        {
            var exists = await conn.DatabaseExists(name);
            if (!exists)
            {
                await new DatabaseSpecification().BuildDatabase(conn, name);
            }

            var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
            {
                Database = name
            };
            ConnectionStrings[name] = builder.ConnectionString;
        }

        // Clean up master database schemas
        try { await conn.DropSchemaAsync("sharded"); } catch { }

        // Clean up tenant database schemas
        foreach (var connStr in ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await cleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    internal static async Task cleanMartenObjectsInPublicSchema(NpgsqlConnection conn)
    {
        try
        {
            // Ensure public schema exists (may have been dropped by a previous test)
            await conn.CreateCommand("CREATE SCHEMA IF NOT EXISTS public").ExecuteNonQueryAsync();

            // Drop all mt_ tables
            var tables = await conn.ExistingTablesAsync();
            foreach (var table in tables.Where(t => t.Schema == "public" && t.Name.StartsWith("mt_")))
            {
                await conn.CreateCommand($"DROP TABLE IF EXISTS {table.QualifiedName} CASCADE").ExecuteNonQueryAsync();
            }

            // Drop all mt_ functions
            var funcs = await conn.CreateCommand(
                "SELECT proname FROM pg_proc p JOIN pg_namespace n ON p.pronamespace = n.oid WHERE n.nspname = 'public' AND p.proname LIKE 'mt_%'")
                .ExecuteReaderAsync();
            var funcNames = new List<string>();
            while (await funcs.ReadAsync()) funcNames.Add(await funcs.GetFieldValueAsync<string>(0));
            await funcs.CloseAsync();
            foreach (var f in funcNames)
            {
                await conn.CreateCommand($"DROP FUNCTION IF EXISTS public.{f} CASCADE").ExecuteNonQueryAsync();
            }

            // Drop all mt_ sequences
            await conn.CreateCommand(
                "DO $$ DECLARE r RECORD; BEGIN FOR r IN (SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' AND sequencename LIKE 'mt_%') LOOP EXECUTE 'DROP SEQUENCE IF EXISTS public.' || r.sequencename || ' CASCADE'; END LOOP; END $$")
                .ExecuteNonQueryAsync();
        }
        catch { }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[Collection("sharded-tenancy")]
public class sharded_tenancy_tests : IAsyncLifetime
{
    private readonly ShardedTenancyFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_tenancy_tests(ShardedTenancyFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Clean schemas before each test
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

    public async Task DisposeAsync()
    {
        if (_store != null)
        {
            _store.Dispose();
        }
    }

    private IDocumentStore CreateStore(Action<ShardedTenancyOptions>? customConfig = null)
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

                customConfig?.Invoke(x);
            });

            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.RegisterDocumentType<Target>();
            opts.RegisterDocumentType<User>();
            opts.Events.AddEventType<ShardedTestEvent>();
        });

        return _store;
    }

    [Fact]
    public async Task can_create_store_and_seed_pool()
    {
        CreateStore();

        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var databases = await sharded.ListDatabasesAsync(CancellationToken.None);

        databases.Count.ShouldBe(3);
        databases.Select(d => d.DatabaseId).OrderBy(x => x)
            .ShouldBe(_fixture.DbNames.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task hash_assignment_distributes_tenants_across_databases()
    {
        CreateStore(); // defaults to hash assignment

        var assignedDatabases = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            var tenantId = $"tenant_{i:000}";
            var dbId = await _store.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
            assignedDatabases.Add(dbId);
        }

        // With 50 tenants and 3 databases, hash should distribute across multiple
        assignedDatabases.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task smallest_assignment_picks_database_with_fewest_tenants()
    {
        CreateStore(x => x.UseSmallestDatabaseAssignment());

        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        // Assign 3 tenants to db A explicitly
        await sharded.AssignTenantAsync("t1", _fixture.DbNames[0], CancellationToken.None);
        await sharded.AssignTenantAsync("t2", _fixture.DbNames[0], CancellationToken.None);
        await sharded.AssignTenantAsync("t3", _fixture.DbNames[0], CancellationToken.None);

        // Assign 1 to db B
        await sharded.AssignTenantAsync("t4", _fixture.DbNames[1], CancellationToken.None);

        // Now auto-assign — should go to db C (0 tenants)
        var tenant5 = await sharded.GetTenantAsync("t5");
        var dbForT5 = await sharded.FindDatabaseForTenantAsync("t5", CancellationToken.None);

        dbForT5.ShouldBe(_fixture.DbNames[2]);
    }

    [Fact]
    public async Task explicit_assignment_throws_for_unknown_tenant()
    {
        CreateStore(x => x.UseExplicitAssignment());

        await Should.ThrowAsync<UnknownTenantIdException>(async () =>
        {
            await _store.Options.Tenancy.GetTenantAsync("unknown_tenant");
        });
    }

    [Fact]
    public async Task explicit_assignment_works_for_pre_assigned_tenant()
    {
        CreateStore(x => x.UseExplicitAssignment());

        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        // Pre-assign
        await sharded.AssignTenantAsync("known_tenant", _fixture.DbNames[0], CancellationToken.None);

        // Should succeed now
        var tenant = await sharded.GetTenantAsync("known_tenant");
        tenant.TenantId.ShouldBe("known_tenant");
    }

    [Fact]
    public async Task partition_created_after_tenant_assignment()
    {
        CreateStore();

        // Apply schema to all databases first
        var databases = await _store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // Assign a tenant
        await _store.Advanced.AddTenantToShardAsync("partition_test_tenant", CancellationToken.None);

        // Find which database it was assigned to
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        var dbId = await sharded.FindDatabaseForTenantAsync("partition_test_tenant", CancellationToken.None);
        dbId.ShouldNotBeNull();

        // Check that PG partitions were created
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();

        var tables = await conn.ExistingTablesAsync();
        // Should have a partition like mt_doc_target_partition_test_tenant
        tables.Any(t => t.Name.Contains("partition_test_tenant")).ShouldBeTrue(
            $"Expected partition for 'partition_test_tenant' in {dbId}. Tables: {string.Join(", ", tables.Select(t => t.QualifiedName))}");
    }

    [Fact]
    public async Task mark_database_full_excludes_from_assignment()
    {
        CreateStore(); // hash assignment

        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        // Mark all but one database as full
        await sharded.MarkDatabaseFullAsync(_fixture.DbNames[0], CancellationToken.None);
        await sharded.MarkDatabaseFullAsync(_fixture.DbNames[1], CancellationToken.None);

        // All new tenants should go to the remaining database
        for (int i = 0; i < 10; i++)
        {
            var dbId = await _store.Advanced.AddTenantToShardAsync($"full_test_{i}", CancellationToken.None);
            dbId.ShouldBe(_fixture.DbNames[2]);
        }
    }

    [Fact]
    public async Task runtime_database_addition()
    {
        // Create store without seeding any databases
        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";
                // No AddDatabase calls — empty pool
            });

            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.RegisterDocumentType<Target>();
        });

        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        // Add databases at runtime
        foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
        {
            await sharded.AddDatabaseAsync(dbName, connStr, CancellationToken.None);
        }

        var databases = await sharded.ListDatabasesAsync(CancellationToken.None);
        databases.Count.ShouldBe(3);
    }

    [Fact]
    public async Task document_crud_across_shards()
    {
        CreateStore();

        // Apply schema
        var databases = await _store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // Add specific tenants
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        await sharded.AssignTenantAsync("alpha", _fixture.DbNames[0], CancellationToken.None);
        await sharded.AssignTenantAsync("beta", _fixture.DbNames[1], CancellationToken.None);

        // Write documents per tenant
        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Store(new Target { Id = Guid.NewGuid(), String = "alpha_data" });
            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession("beta"))
        {
            session.Store(new Target { Id = Guid.NewGuid(), String = "beta_data" });
            await session.SaveChangesAsync();
        }

        // Read back — isolation check
        await using (var q1 = _store.QuerySession("alpha"))
        {
            var results = await q1.Query<Target>().ToListAsync();
            results.Count.ShouldBe(1);
            results[0].String.ShouldBe("alpha_data");
        }

        await using (var q2 = _store.QuerySession("beta"))
        {
            var results = await q2.Query<Target>().ToListAsync();
            results.Count.ShouldBe(1);
            results[0].String.ShouldBe("beta_data");
        }
    }

    [Fact]
    public async Task event_append_and_query_across_shards()
    {
        CreateStore();

        var databases = await _store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        await sharded.AssignTenantAsync("ev_alpha", _fixture.DbNames[0], CancellationToken.None);
        await sharded.AssignTenantAsync("ev_beta", _fixture.DbNames[1], CancellationToken.None);

        Guid streamA, streamB;

        // Append events
        await using (var session = _store.LightweightSession("ev_alpha"))
        {
            streamA = session.Events.StartStream<ShardedTestEvent>(
                new ShardedTestEvent { Value = "a1" },
                new ShardedTestEvent { Value = "a2" }).Id;
            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession("ev_beta"))
        {
            streamB = session.Events.StartStream<ShardedTestEvent>(
                new ShardedTestEvent { Value = "b1" }).Id;
            await session.SaveChangesAsync();
        }

        // Query events per tenant
        await using (var q1 = _store.QuerySession("ev_alpha"))
        {
            var events = await q1.Events.FetchStreamAsync(streamA);
            events.Count.ShouldBe(2);
        }

        await using (var q2 = _store.QuerySession("ev_beta"))
        {
            var events = await q2.Events.FetchStreamAsync(streamB);
            events.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task build_databases_returns_all_pool_databases()
    {
        CreateStore();

        var databases = await _store.Options.Tenancy.BuildDatabases();

        // Should include the pool lookup DB + 3 shard databases
        databases.Count.ShouldBeGreaterThanOrEqualTo(4);

        var dbNames = databases
            .Where(d => d.Identifier != "ShardedTenancyPool")
            .Select(d => d.Identifier)
            .OrderBy(x => x)
            .ToArray();

        dbNames.ShouldBe(_fixture.DbNames.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task tenant_count_updates_correctly()
    {
        CreateStore();

        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        await sharded.AssignTenantAsync("count_t1", _fixture.DbNames[0], CancellationToken.None);
        await sharded.AssignTenantAsync("count_t2", _fixture.DbNames[0], CancellationToken.None);
        await sharded.AssignTenantAsync("count_t3", _fixture.DbNames[1], CancellationToken.None);

        var databases = await sharded.ListDatabasesAsync(CancellationToken.None);

        var dbA = databases.First(d => d.DatabaseId == _fixture.DbNames[0]);
        var dbB = databases.First(d => d.DatabaseId == _fixture.DbNames[1]);
        var dbC = databases.First(d => d.DatabaseId == _fixture.DbNames[2]);

        dbA.TenantCount.ShouldBe(2);
        dbB.TenantCount.ShouldBe(1);
        dbC.TenantCount.ShouldBe(0);
    }
}

public class ShardedTestEvent
{
    public string Value { get; set; } = "";
}
