#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// #4617 section 4 deferred — pin async projection daemon behavior under the
/// combination of <see cref="ShardedTenancy"/> + <c>UseTenantPartitionedEvents</c>.
///
/// <para>
/// Each shard is its own <see cref="MartenDatabase"/>; under per-tenant event
/// partitioning the daemon must walk every known shard and process each shard's
/// tenants. Two tenants on TWO different shards must both reach non-stale
/// projection state when <c>StartAllAsync</c> runs.
/// </para>
///
/// <para>
/// Pins:
/// <list type="bullet">
///   <item>After daemon catch-up, each tenant's projection doc is materialized
///     against its own shard's database — proven by reading from a tenant-
///     scoped query session and getting the right counts.</item>
///   <item>The projection-progression row lives in the SAME shard's
///     <c>mt_event_progression</c> table as the tenant's events — no
///     cross-shard progression coordination.</item>
/// </list>
/// </para>
///
/// <para>
/// Own-store: this test needs explicit per-shard tenant assignment via
/// <c>AddTenantToShardAsync</c>, which is awkward to share across siblings.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class sharded_daemon_per_shard_progression : IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_daemon_per_shard_progression(ShardedPartitionedFixture fixture)
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
            await ShardedPartitionedFixture.CleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task daemon_catches_up_tenants_on_separate_shards_independently()
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
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedDaemonEvent>();

            // Async projection that the daemon will catch up across shards.
            opts.Projections.Add<ShardedDaemonProjection>(ProjectionLifecycle.Async);

            opts.Schema.For<ShardedDaemonCounter>().DocumentAlias("p2c_sdc");
        });

        // Assign each tenant to a DIFFERENT shard to exercise the cross-shard
        // daemon catch-up path explicitly.
        var shardA = _fixture.DbNames[0];
        var shardB = _fixture.DbNames[1];
        await _store.Advanced.AddTenantToShardAsync("tenant_on_a", shardA, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("tenant_on_b", shardB, CancellationToken.None);

        var aStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("tenant_on_a"))
        {
            session.Events.StartStream<ShardedDaemonCounter>(aStream,
                new ShardedDaemonEvent("a-1"),
                new ShardedDaemonEvent("a-2"),
                new ShardedDaemonEvent("a-3"));
            await session.SaveChangesAsync();
        }

        var bStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("tenant_on_b"))
        {
            session.Events.StartStream<ShardedDaemonCounter>(bStream,
                new ShardedDaemonEvent("b-1"),
                new ShardedDaemonEvent("b-2"));
            await session.SaveChangesAsync();
        }

        // Drive ONE daemon per shard — under sharded tenancy each shard is its
        // own MartenDatabase and BuildProjectionDaemonAsync needs an explicit
        // tenant or database identifier to know which database to run against.
        // The default-tenant overload throws DefaultTenantUsageDisabledException
        // when the store is sharded.
        using var daemonA = await _store.BuildProjectionDaemonAsync("tenant_on_a");
        using var daemonB = await _store.BuildProjectionDaemonAsync("tenant_on_b");
        await daemonA.StartAllAsync();
        await daemonB.StartAllAsync();
        await daemonA.WaitForNonStaleData(15.Seconds());
        await daemonB.WaitForNonStaleData(15.Seconds());

        // Pin 1: each tenant's projection doc reflects ONLY its own event count.
        await using (var query = _store.QuerySession("tenant_on_a"))
        {
            var doc = await query.LoadAsync<ShardedDaemonCounter>(aStream);
            doc.ShouldNotBeNull("tenant_on_a's projection must materialize on shard A");
            doc!.EventCount.ShouldBe(3, "tenant_on_a appended 3 events — tenant_on_b's must not bleed");
        }
        await using (var query = _store.QuerySession("tenant_on_b"))
        {
            var doc = await query.LoadAsync<ShardedDaemonCounter>(bStream);
            doc.ShouldNotBeNull("tenant_on_b's projection must materialize on shard B");
            doc!.EventCount.ShouldBe(2, "tenant_on_b appended 2 events — tenant_on_a's must not bleed");
        }
    }

    [Fact]
    public async Task progression_row_for_each_tenant_lives_on_its_own_shard()
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
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedDaemonEvent>();
            opts.Projections.Add<ShardedDaemonProjection>(ProjectionLifecycle.Async);
            opts.Schema.For<ShardedDaemonCounter>().DocumentAlias("p2c_sdc");
        });

        var shardA = _fixture.DbNames[0];
        var shardB = _fixture.DbNames[2];
        await _store.Advanced.AddTenantToShardAsync("progress_a", shardA, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("progress_c", shardB, CancellationToken.None);

        var aStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("progress_a"))
        {
            session.Events.StartStream<ShardedDaemonCounter>(aStream,
                new ShardedDaemonEvent("a"));
            await session.SaveChangesAsync();
        }
        var cStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("progress_c"))
        {
            session.Events.StartStream<ShardedDaemonCounter>(cStream,
                new ShardedDaemonEvent("c"));
            await session.SaveChangesAsync();
        }

        using var daemonA = await _store.BuildProjectionDaemonAsync("progress_a");
        using var daemonC = await _store.BuildProjectionDaemonAsync("progress_c");
        await daemonA.StartAllAsync();
        await daemonC.StartAllAsync();
        await daemonA.WaitForNonStaleData(15.Seconds());
        await daemonC.WaitForNonStaleData(15.Seconds());

        // Pin: each shard's public schema has its own mt_event_progression
        // table with a row for the projection. The PROJECTION's progression
        // row name is the projection name (or composed with tenant id when
        // per-tenant tracking is on); the row's last_seq_id must be > 0
        // because at least one event flowed.
        var shardAConnStr = _fixture.ConnectionStrings[shardA];
        var shardBConnStr = _fixture.ConnectionStrings[shardB];

        var rowsOnA = await ReadProgressionRowsAsync(shardAConnStr, ShardedDaemonProjection.ProjectionName);
        var rowsOnB = await ReadProgressionRowsAsync(shardBConnStr, ShardedDaemonProjection.ProjectionName);

        rowsOnA.Count.ShouldBeGreaterThan(0,
            "shard A must have a projection progression row for tenant-on-A's catch-up");
        rowsOnB.Count.ShouldBeGreaterThan(0,
            "shard C must have a projection progression row for tenant-on-C's catch-up");

        // Sanity: every row's last_seq_id > 0 (the daemon actually advanced).
        rowsOnA.ShouldAllBe(r => r.LastSeqId > 0);
        rowsOnB.ShouldAllBe(r => r.LastSeqId > 0);
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<(string Name, long LastSeqId)>>
        ReadProgressionRowsAsync(string connectionString, string projectionName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Each shard's events live in its `public` schema (the sharded model
        // hosts mt_events / mt_event_progression in the per-shard public schema
        // when the doc-side schema is `sharded`/`tenants`). Filter to rows
        // whose name starts with the projection name.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select name, last_seq_id from public.mt_event_progression where name like @n order by name";
        cmd.Parameters.AddWithValue("n", projectionName + "%");
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new System.Collections.Generic.List<(string, long)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }
        return rows;
    }
}

public record ShardedDaemonEvent(string Label);

public class ShardedDaemonCounter
{
    public Guid Id { get; set; }
    public int EventCount { get; set; }
}

public partial class ShardedDaemonProjection : SingleStreamProjection<ShardedDaemonCounter, Guid>
{
    public const string ProjectionName = "ShardedDaemonCounter";
    public ShardedDaemonProjection() { Name = ProjectionName; }

    public void Apply(ShardedDaemonCounter c, ShardedDaemonEvent _) => c.EventCount++;
}
