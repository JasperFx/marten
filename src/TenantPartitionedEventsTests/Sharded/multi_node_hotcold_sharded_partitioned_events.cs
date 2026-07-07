#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;
using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// JasperFx/jasperfx#486 WS1 — native HotCold distribution matrix, cell "sharded/pooled databases +
/// UseTenantPartitionedEvents": TWO shard databases, two tenants per shard (different heights, so
/// their per-tenant sequences overlap), two async projections, TWO IHost nodes both running
/// <c>AddAsyncDaemon(DaemonMode.HotCold)</c> with the same DaemonLockId.
///
/// <para>
/// DISTRIBUTION GRANULARITY (JasperFx.Events 2.21.0, jasperfx#491 + Marten #4862): non-default
/// tenancy routes the coordinator to <c>MultiTenantedProjectionDistributor</c> — ONE
/// advisory-lock-guarded <c>IProjectionSet</c> PER DATABASE. So a shard database's projections run
/// WHOLE on exactly one node (unlike the single-DB matrix cell, where the SingleTenant distributor
/// can split individual projections across nodes), while different shard databases can land on
/// different nodes.
/// </para>
///
/// <para>
/// Because the store is tenant-partitioned (<c>IEventStore.DistributesAgentsPerTenant</c>), each
/// database's set now expands its store-global shard names into per-tenant
/// <c>{Projection}:All:{tenant}</c> ShardNames from THAT shard database's own tenant registry
/// (<c>PerTenantShardExpansion</c> over <c>ICrossTenantRebuildSource</c>), and the winning node
/// starts each tenant-bearing identity through <c>StartAgentAsync</c>'s per-tenant branch
/// (jasperfx#487). The WS1 per-tenant baseline is therefore met per shard: per-tenant progression
/// rows reach each tenant's OWN height and <c>HighWaterMark:{tenant}</c> rows appear in the shard
/// database's own <c>mt_event_progression</c>, with no store-global agent ever running. This test
/// previously pinned the pre-#491 store-global-only behavior; the assertions were flipped when
/// the fix landed.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public partial class multi_node_hotcold_sharded_partitioned_events: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;

    public multi_node_hotcold_sharded_partitioned_events(
        ShardedPartitionedFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public class ShTrip { public Guid Id { get; set; } public double Distance { get; set; } }
    public class ShTally { public Guid Id { get; set; } public int Count { get; set; } }

    public record ShStarted(Guid Id);
    public record ShLeg(double Distance);

    public partial class ShTripProjection: SingleStreamProjection<ShTrip, Guid>
    {
        public const string ProjectionName = "Ws1ShTrip";
        public ShTripProjection() => Name = ProjectionName;
        public void Apply(ShTrip t, ShLeg e) => t.Distance += e.Distance;
    }

    public partial class ShTallyProjection: SingleStreamProjection<ShTally, Guid>
    {
        public const string ProjectionName = "Ws1ShTally";
        public ShTallyProjection() => Name = ProjectionName;
        public void Apply(ShTally t, ShLeg e) => t.Count++;
    }

    private const int LockId = 48617;

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

    public Task DisposeAsync() => Task.CompletedTask;

    private void Configure(StoreOptions opts)
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
        opts.DisableNpgsqlLogging = true;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        opts.Events.UseTenantPartitionedEvents = true;

        opts.Projections.DaemonLockId = LockId;
        opts.Projections.LeadershipPollingTime = 250;

        opts.Schema.For<ShTrip>().DocumentAlias("ws1sh_trip");
        opts.Schema.For<ShTally>().DocumentAlias("ws1sh_tally");

        opts.Projections.Add<ShTripProjection>(ProjectionLifecycle.Async);
        opts.Projections.Add<ShTallyProjection>(ProjectionLifecycle.Async);
    }

    [Fact]
    public async Task two_hotcold_nodes_own_whole_shard_databases_with_per_tenant_fan_out()
    {
        var shardA = _fixture.DbNames[0];
        var shardB = _fixture.DbNames[1];

        // Two tenants per shard, different heights so the per-tenant sequences overlap within a
        // shard. 1 ShStarted + N ShLeg per tenant.
        var tenantsByShard = new Dictionary<string, Dictionary<string, int>>
        {
            [shardA] = new() { ["ws1sh_a1"] = 4, ["ws1sh_a2"] = 2 },
            [shardB] = new() { ["ws1sh_b1"] = 3, ["ws1sh_b2"] = 5 }
        };
        var expectedHeight = tenantsByShard.Values.SelectMany(x => x)
            .ToDictionary(p => p.Key, p => (long)(p.Value + 1));
        var shardHighWater = tenantsByShard.ToDictionary(
            p => p.Key, p => p.Value.Values.Max(v => (long)(v + 1)));

        // Bootstrap store provisions tenants + events BEFORE any node starts, and stays alive for
        // the doc-side verification at the end.
        var streams = new Dictionary<string, Guid>();
        using var bootstrap = (DocumentStore)DocumentStore.For(Configure);

        // Materialize the FULL schema (including the projection document tables) on every shard
        // database BEFORE registering tenants, so AddTenantToShardAsync adds each tenant's list
        // partition to the doc tables too. This ordering matters: a table that comes into existence
        // AFTER its tenants joined the shard is created with ZERO partitions — the lazy/one-shot
        // table creation on a fresh store instance does not hydrate partitions from the shard's own
        // tenants.mt_tenant_partitions registry (observed: the HotCold nodes' daemons created
        // mt_doc_* as empty partitioned tables and every projection write failed with 23514 "no
        // partition of relation found for row"). That fresh-node provisioning gap is a product
        // follow-up in the #4682/#4706 per-database-partition-state family, not this test's subject.
        await bootstrap.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        foreach (var (shard, tenants) in tenantsByShard)
        {
            foreach (var (tenant, legs) in tenants)
            {
                await bootstrap.Advanced.AddTenantToShardAsync(tenant, shard, CancellationToken.None);

                var id = Guid.NewGuid();
                streams[tenant] = id;
                await using var session = bootstrap.LightweightSession(tenant);
                var events = new object[] { new ShStarted(id) }
                    .Concat(Enumerable.Range(0, legs).Select(_ => (object)new ShLeg(1.0)))
                    .ToArray();
                session.Events.StartStream<ShTrip>(id, events);
                await session.SaveChangesAsync();
            }
        }

        // TWO nodes in HotCold contention over the same shard pool + lock id.
        using var nodeA = await StartNodeAsync();
        using var nodeB = await StartNodeAsync();

        try
        {
            var projections = new[] { ShTripProjection.ProjectionName, ShTallyProjection.ProjectionName };

            // Deterministic wait on per-shard progression state: every (projection, tenant) pair on
            // each shard database must reach that tenant's OWN height (the WS1 per-tenant baseline),
            // and the shard's global high-water row its own max.
            var rowsByShard = new Dictionary<string, List<(string Name, long Seq)>>();
            foreach (var shard in new[] { shardA, shardB })
            {
                var shardTenants = tenantsByShard[shard];
                rowsByShard[shard] = await WaitForProgressionAsync(
                    _fixture.ConnectionStrings[shard], r =>
                        shardTenants.Keys.All(t =>
                            SeqOf(r, $"{ShTripProjection.ProjectionName}:All:{t}") >= expectedHeight[t] &&
                            SeqOf(r, $"{ShTallyProjection.ProjectionName}:All:{t}") >= expectedHeight[t] &&
                            SeqOf(r, $"HighWaterMark:{t}") >= expectedHeight[t]) &&
                        SeqOf(r, "HighWaterMark") >= shardHighWater[shard],
                    60.Seconds(), $"per-tenant shards reach their own heights on {shard}");
                DumpRows($"{shard} mt_event_progression once caught up", rowsByShard[shard]);
            }

            // ---- Placement: whole shard database per node, per-tenant agents inside ----
            foreach (var shard in new[] { shardA, shardB })
            {
                // jasperfx#491: the shard database's set expands into per-tenant identities from
                // THAT database's own tenant registry — its winner runs one agent per
                // (projection, tenant-on-this-shard), and no store-global agent exists.
                var perTenantIdentities = projections
                    .SelectMany(p => tenantsByShard[shard].Keys.Select(t => $"{p}:All:{t}"))
                    .ToArray();

                var onA = await AgentIdentitiesAsync(nodeA, shard);
                var onB = await AgentIdentitiesAsync(nodeB, shard);
                _output.WriteLine($"{shard}: node A runs [{string.Join(", ", onA)}], " +
                                  $"node B runs [{string.Join(", ", onB)}]");

                // Only tenant-bearing agents run, and only for THIS shard's tenants — the
                // expansion never leaks another shard database's tenants into this set.
                onA.Concat(onB).ShouldAllBe(identity => perTenantIdentities.Contains(identity));

                // Per-database lock granularity: exactly one node owns the shard database, and it
                // runs ALL of the database's per-tenant agents — the set is never split across
                // nodes. Which node wins which database is a lock race (the two databases CAN land
                // on different nodes); assert exclusivity + completeness, not the split.
                var owners = new[] { onA.Any(), onB.Any() }.Count(x => x);
                owners.ShouldBe(1, $"{shard} must be owned by exactly one of the two HotCold nodes");

                var winner = onA.Any() ? onA : onB;
                winner.OrderBy(x => x).ShouldBe(perTenantIdentities.OrderBy(x => x),
                    $"the node owning {shard} must run one agent per (projection, tenant) " +
                    "(MultiTenantedProjectionDistributor distributes whole databases)");
            }

            // ---- WS1 per-tenant baseline: met per shard as of jasperfx#491 ----
            foreach (var shard in new[] { shardA, shardB })
            {
                var rows = rowsByShard[shard];
                var perTenantIdentities = projections
                    .SelectMany(p => tenantsByShard[shard].Keys.Select(t => $"{p}:All:{t}"))
                    .ToArray();

                rows.Where(r => r.Name.StartsWith("Ws1Sh", StringComparison.Ordinal))
                    .ShouldAllBe(r => perTenantIdentities.Contains(r.Name),
                        $"per-tenant fan-out on {shard}: every projection progression row is " +
                        "tenant-bearing (no store-global {Projection}:All row) and belongs to one " +
                        "of this shard's own tenants");

                foreach (var tenant in tenantsByShard[shard].Keys)
                {
                    SeqOf(rows, $"{ShTripProjection.ProjectionName}:All:{tenant}")
                        .ShouldBe(expectedHeight[tenant],
                            $"tenant {tenant}'s trip shard tracks the tenant's OWN height on {shard}");
                    SeqOf(rows, $"{ShTallyProjection.ProjectionName}:All:{tenant}")
                        .ShouldBe(expectedHeight[tenant],
                            $"tenant {tenant}'s tally shard tracks the tenant's OWN height on {shard}");
                    SeqOf(rows, $"HighWaterMark:{tenant}").ShouldBe(expectedHeight[tenant],
                        $"tenant {tenant}'s own high-water row is persisted on {shard}");
                }

                SeqOf(rows, "HighWaterMark").ShouldBe(shardHighWater[shard],
                    $"{shard}'s store-global high water = max over its tenants' overlapping heights");
            }

            // ---- Docs materialize per tenant on the right shard at this small scale ----
            foreach (var (tenant, legs) in tenantsByShard.Values.SelectMany(x => x))
            {
                await using var query = bootstrap.QuerySession(tenant);
                var trip = await query.LoadAsync<ShTrip>(streams[tenant]);
                trip.ShouldNotBeNull($"tenant {tenant}'s ShTrip doc must materialize on its shard");
                trip!.Distance.ShouldBe(legs);
                var tally = await query.LoadAsync<ShTally>(streams[tenant]);
                tally.ShouldNotBeNull($"tenant {tenant}'s ShTally doc must materialize on its shard");
                tally!.Count.ShouldBe(legs);
            }
        }
        finally
        {
            await nodeA.StopAsync();
            await nodeB.StopAsync();
        }
    }

    private async Task<IHost> StartNodeAsync()
    {
        return await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(Configure).AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();
    }

    private static async Task<IReadOnlyList<string>> AgentIdentitiesAsync(IHost node, string dbName)
    {
        var coordinator = node.Services.GetRequiredService<IProjectionCoordinator>();
        var daemon = await coordinator.DaemonForDatabase(dbName);
        return daemon.CurrentAgents()
            .Where(x => x.Status == AgentStatus.Running)
            .Select(x => x.Name.Identity)
            .OrderBy(x => x)
            .ToList();
    }

    private async Task<List<(string Name, long Seq)>> WaitForProgressionAsync(
        string connectionString, Func<List<(string Name, long Seq)>, bool> condition,
        TimeSpan timeout, string what)
    {
        var sw = Stopwatch.StartNew();
        List<(string Name, long Seq)> rows = new();
        while (sw.Elapsed < timeout)
        {
            rows = await ProgressionRowsAsync(connectionString);
            if (condition(rows))
            {
                return rows;
            }

            await Task.Delay(250);
        }

        DumpRows($"TIMED OUT waiting for: {what}", rows);
        throw new TimeoutException($"Timed out after {timeout} waiting for: {what}");
    }

    private void DumpRows(string label, List<(string Name, long Seq)> rows)
    {
        _output.WriteLine($"=== {label} ===");
        foreach (var (name, seq) in rows.OrderBy(r => r.Name))
        {
            _output.WriteLine($"{seq,6} | {name}");
        }
    }

    private static long SeqOf(List<(string Name, long Seq)> rows, string name) =>
        rows.FirstOrDefault(r => r.Name == name).Seq;

    private static async Task<List<(string Name, long Seq)>> ProgressionRowsAsync(
        string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            "select name, coalesce(last_seq_id,0) from public.mt_event_progression order by name");
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, long)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }
}
