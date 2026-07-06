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
/// DISTRIBUTION GRANULARITY OBSERVED AND PINNED HERE (JasperFx.Events 2.20.0 + Marten master):
/// non-default tenancy routes the coordinator to <c>MultiTenantedProjectionDistributor</c> — ONE
/// advisory-lock-guarded <c>IProjectionSet</c> PER DATABASE carrying ALL the store-global shard
/// names. So a shard database's projections run WHOLE on exactly one node (unlike the single-DB
/// matrix cell, where the SingleTenant distributor can split individual projections across nodes),
/// while different shard databases can land on different nodes.
/// </para>
///
/// <para>
/// As in the single-database cell, the winning node hands each STORE-GLOBAL identity
/// (<c>{Projection}:All</c>) to <c>StartAgentAsync</c>, whose exact-identity path builds the
/// store-global agent — the per-tenant fan-out only runs inside <c>StartAllAsync</c>, which the
/// native coordinator never calls. The WS1 per-tenant baseline (<c>{Projection}:All:{tenant}</c> +
/// <c>HighWaterMark:{tenant}</c> rows per shard) is therefore NOT met on this path today, even
/// though Marten explicitly marks this topology as needing per-tenant agents
/// (<c>IEventStore.DistributesAgentsPerTenant</c> is true for ShardedTenancy +
/// UseTenantPartitionedEvents — but only Wolverine-style external distributors consume it). When
/// epic #486 teaches the native coordinator the per-tenant shape, flip the per-tenant assertions
/// below from absent to present.
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
    public async Task two_hotcold_nodes_own_whole_shard_databases_without_per_tenant_fan_out()
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
            // Deterministic wait on per-shard progression state: each shard database's store-global
            // shards must reach that shard's own high water (max over its overlapping per-tenant
            // sequences).
            var rowsByShard = new Dictionary<string, List<(string Name, long Seq)>>();
            foreach (var shard in new[] { shardA, shardB })
            {
                rowsByShard[shard] = await WaitForProgressionAsync(
                    _fixture.ConnectionStrings[shard], r =>
                        SeqOf(r, $"{ShTripProjection.ProjectionName}:All") >= shardHighWater[shard] &&
                        SeqOf(r, $"{ShTallyProjection.ProjectionName}:All") >= shardHighWater[shard],
                    60.Seconds(), $"store-global shards reach high water {shardHighWater[shard]} on {shard}");
                DumpRows($"{shard} mt_event_progression once caught up", rowsByShard[shard]);
            }

            // ---- Placement: whole shard database per node ----
            var allShardIdentities = new[]
            {
                $"{ShTripProjection.ProjectionName}:All", $"{ShTallyProjection.ProjectionName}:All"
            };

            foreach (var shard in new[] { shardA, shardB })
            {
                var onA = await AgentIdentitiesAsync(nodeA, shard);
                var onB = await AgentIdentitiesAsync(nodeB, shard);
                _output.WriteLine($"{shard}: node A runs [{string.Join(", ", onA)}], " +
                                  $"node B runs [{string.Join(", ", onB)}]");

                // Only store-global agents run — no tenant-bearing agent identities on either node.
                onA.Concat(onB).ShouldAllBe(identity => allShardIdentities.Contains(identity));

                // Per-database lock granularity: exactly one node owns the shard database, and it
                // runs ALL of the database's shards — the set is never split across nodes. Which
                // node wins which database is a lock race (the two databases CAN land on different
                // nodes); assert exclusivity + completeness, not the split.
                var owners = new[] { onA.Any(), onB.Any() }.Count(x => x);
                owners.ShouldBe(1, $"{shard} must be owned by exactly one of the two HotCold nodes");

                var winner = onA.Any() ? onA : onB;
                winner.OrderBy(x => x).ShouldBe(allShardIdentities.OrderBy(x => x),
                    $"the node owning {shard} must run ALL of that database's shards " +
                    "(MultiTenantedProjectionDistributor distributes whole databases)");
            }

            // ---- WS1 per-tenant baseline: NOT met on this path today (see class doc) ----
            foreach (var shard in new[] { shardA, shardB })
            {
                var rows = rowsByShard[shard];
                rows.Where(r => r.Name.StartsWith("Ws1Sh", StringComparison.Ordinal))
                    .ShouldAllBe(r => allShardIdentities.Contains(r.Name),
                        $"current behavior on {shard}: only store-global progression rows exist " +
                        "under the native HotCold coordinator — no {Projection}:All:{tenant} rows. " +
                        "If per-tenant rows now appear, the native coordinator gained per-tenant " +
                        "fan-out and this test should assert the WS1 per-tenant baseline instead");
                rows.Any(r => r.Name.StartsWith("HighWaterMark:", StringComparison.Ordinal))
                    .ShouldBeFalse(
                        $"current behavior on {shard}: no per-tenant HighWaterMark rows without " +
                        "tenant-bearing agents");
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

            _ = expectedHeight; // per-tenant heights are documented above; the per-tenant rows that
                                // would carry them do not exist on this path today.
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
