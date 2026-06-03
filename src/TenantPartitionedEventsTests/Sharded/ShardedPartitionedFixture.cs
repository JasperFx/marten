#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// xUnit collection for sharded-tenancy partitioned tests in this project. The
/// collection id is intentionally NOT the same as MultiTenancyTests' own
/// "sharded-tenancy" — xUnit collections are per-assembly, but using a
/// distinct name removes any ambiguity for readers cross-referencing the two
/// projects. <see cref="DisableParallelization"/> stays true (the 3 physical
/// shard databases are single-master resources; concurrent tests would race
/// over per-database schema state).
/// </summary>
[CollectionDefinition("sharded-tenant-partitioned", DisableParallelization = true)]
public sealed class ShardedPartitionedCollection : ICollectionFixture<ShardedPartitionedFixture>;

/// <summary>
/// Clone of MultiTenancyTests/sharded_tenancy_tests.cs's <c>ShardedTenancyFixture</c>
/// — provisions the 3 physical shard databases (<c>marten_shard_a/b/c</c>) on
/// the master connection, drops the well-known per-test schemas + every
/// <c>mt_*</c> object from their public schemas, and exposes the per-shard
/// connection strings for tests to use. Kept as a copy in this project so
/// MultiTenancyTests doesn't have to grow a public extraction; the cloned
/// fixture is internal to the per-tenant-partitioning test surface.
/// </summary>
public sealed class ShardedPartitionedFixture : IAsyncLifetime
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
            await CleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    /// <summary>
    /// Wipe every <c>mt_*</c> table / function / sequence from the public schema
    /// of the given (shard) connection. Used by the fixture init and reused by
    /// individual tests that need to scrub a specific shard mid-suite.
    /// </summary>
    public static async Task CleanMartenObjectsInPublicSchema(NpgsqlConnection conn)
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
