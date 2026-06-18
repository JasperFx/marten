#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// #4763 — `ShardedTenancy` tenant-assignment bookkeeping drifts: re-assigning a tenant from shard A
/// to shard B recomputes only the TARGET shard's `tenant_count` (`AssignTenantAsync`, ShardedTenancy.cs
/// :337-340), never decrements the SOURCE shard. So shard A's count stays inflated forever, which makes
/// `UseSmallestDatabaseAssignment` mis-rank shards (it favours A, which only *looks* fuller).
///
/// <para>This test moves one tenant A→B and asserts the pool's `tenant_count` for A reflects reality
/// (0 active tenants). It fails because A's count remains 1.</para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4763_reassign_count_divergence: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;
    private DocumentStore _store = null!;

    public Bug_4763_reassign_count_divergence(ShardedPartitionedFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
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
            await ShardedPartitionedFixture.CleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public async Task DisposeAsync()
    {
        if (_store != null!) await _store.DisposeAsync();
    }

    [Fact]
    public async Task reassigning_a_tenant_decrements_the_source_shard_count()
    {
        _store = (DocumentStore)DocumentStore.For(opts =>
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
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedDaemonEvent>();
            opts.Schema.For<ShardedDaemonCounter>().DocumentAlias("p4763_sdc");
        });

        var shardA = _fixture.DbNames[0];
        var shardB = _fixture.DbNames[1];

        // Assign the tenant to A, then move it to B.
        await _store.Advanced.AddTenantToShardAsync("mover", shardA, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("mover", shardB, CancellationToken.None);

        var counts = await ReadPoolCountsAsync();
        _output.WriteLine("pool tenant_count per database_id: " +
                          string.Join(", ", counts.Count == 0 ? new[] { "(none)" } : Array.Empty<string>()));
        foreach (var (db, count) in counts) _output.WriteLine($"  {db} = {count}");

        // The tenant now lives on B, so A has 0 active tenants and B has 1.
        counts.ShouldContainKey(shardB);
        counts[shardB].ShouldBe(1, "target shard B has the moved tenant");
        counts.ShouldContainKey(shardA);
        counts[shardA].ShouldBe(0,
            "source shard A should be decremented when the tenant moves away — it is not (stays 1), " +
            "so UseSmallestDatabaseAssignment keeps treating A as fuller than it is");
    }

    /// <summary>Read the sharded pool's per-database tenant_count from the master/pool DB, discovering
    /// the pool table via information_schema so the test does not depend on the internal table name.</summary>
    private static async Task<Dictionary<string, int>> ReadPoolCountsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        string? poolTable = null;
        await using (var find = await conn
                         .CreateCommand(
                             "select table_name from information_schema.columns " +
                             "where table_schema = 'sharded' and column_name = 'tenant_count' limit 1")
                         .ExecuteReaderAsync())
        {
            if (await find.ReadAsync()) poolTable = find.GetString(0);
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (poolTable == null) return result;

        await using var reader = await conn
            .CreateCommand($"select database_id, tenant_count from sharded.{poolTable}")
            .ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = Convert.ToInt32(reader.GetValue(1));
        }
        return result;
    }
}
