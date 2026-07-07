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
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;
using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// JasperFx/jasperfx#486 WS1 — native HotCold distribution matrix, cell "single database +
/// UseTenantPartitionedEvents": ONE conjoined per-tenant-partitioned database, TWO IHost nodes both
/// running <c>AddAsyncDaemon(DaemonMode.HotCold)</c> against the same schema + DaemonLockId, with two
/// async projections and three tenants of different heights.
///
/// <para>
/// DISTRIBUTION GRANULARITY (JasperFx.Events 2.21.0, jasperfx#491 + Marten #4862): with
/// DefaultTenancy the coordinator builds a <c>SingleTenantProjectionDistributor</c> — one
/// advisory-lock-guarded <c>IProjectionSet</c> per STORE-GLOBAL shard name. Because the store is
/// tenant-partitioned (<c>IEventStore.DistributesAgentsPerTenant</c>), the distributor now expands
/// each store-global name into per-tenant <c>{Projection}:All:{tenant}</c> ShardNames at
/// distribution-build time (<c>PerTenantShardExpansion</c>), and the winning node starts each
/// tenant-bearing identity through <c>StartAgentAsync</c>'s per-tenant branch (jasperfx#487). So:
/// <list type="bullet">
///   <item>Lock granularity is unchanged — still one advisory lock per STORE-GLOBAL shard, so a
///     projection's per-tenant agents all ride together on whichever node wins that projection's
///     lock; different projections may still land on different nodes.</item>
///   <item>The WS1 per-tenant baseline IS now met on this path: per-tenant
///     <c>{Projection}:All:{tenant}</c> progression rows reach each tenant's OWN height (not the
///     store-global max), <c>HighWaterMark:{tenant}</c> rows are persisted per tenant, and no
///     store-global <c>{Projection}:All</c> agent ever runs (the expansion happens before the
///     first agent start, and coordinator reconciliation would retire one anyway).</item>
/// </list>
/// This test previously pinned the pre-#491 store-global-only behavior; the assertions were
/// flipped to the per-tenant baseline when the fix landed.
/// </para>
/// </summary>
public partial class multi_node_hotcold_single_database_partitioned_events
{
    private readonly ITestOutputHelper _output;

    public multi_node_hotcold_single_database_partitioned_events(ITestOutputHelper output)
        => _output = output;

    public class HcTrip { public Guid Id { get; set; } public double Distance { get; set; } }
    public class HcTally { public Guid Id { get; set; } public int Count { get; set; } }

    public record HcStarted(Guid Id);
    public record HcLeg(double Distance);

    public partial class HcTripProjection: SingleStreamProjection<HcTrip, Guid>
    {
        public const string ProjectionName = "Ws1HcTrip";
        public HcTripProjection() => Name = ProjectionName;
        public void Apply(HcTrip t, HcLeg e) => t.Distance += e.Distance;
    }

    public partial class HcTallyProjection: SingleStreamProjection<HcTally, Guid>
    {
        public const string ProjectionName = "Ws1HcTally";
        public HcTallyProjection() => Name = ProjectionName;
        public void Apply(HcTally t, HcLeg e) => t.Count++;
    }

    private static readonly string Schema = $"ws1_hcold_p{Environment.ProcessId}";
    private const int LockId = 48611;

    private static void Configure(StoreOptions o)
    {
        o.Connection(ConnectionSource.ConnectionString);
        o.DatabaseSchemaName = Schema;
        o.DisableNpgsqlLogging = true;
        o.Events.TenancyStyle = TenancyStyle.Conjoined;
        o.Events.UseTenantPartitionedEvents = true;
        o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        o.Policies.AllDocumentsAreMultiTenanted();

        o.Projections.DaemonLockId = LockId;
        o.Projections.LeadershipPollingTime = 250;

        o.Schema.For<HcTrip>().DocumentAlias("ws1_hc_trip");
        o.Schema.For<HcTally>().DocumentAlias("ws1_hc_tally");

        o.Projections.Add<HcTripProjection>(ProjectionLifecycle.Async);
        o.Projections.Add<HcTallyProjection>(ProjectionLifecycle.Async);
    }

    [Fact]
    public async Task two_hotcold_nodes_fan_out_per_tenant_agents_under_store_global_shard_locks()
    {
        // Seed everything with a bootstrap store BEFORE any node starts, so the two hosts never race
        // on schema creation (the partitioned-schema path is the known 42P07/23505 hot spot).
        var tenants = new Dictionary<string, int> { ["hc_t1"] = 5, ["hc_t2"] = 4, ["hc_t3"] = 3 };
        var streams = new Dictionary<string, Guid>();

        using (var bootstrap = (DocumentStore)DocumentStore.For(Configure))
        {
            await bootstrap.Advanced.Clean.CompletelyRemoveAllAsync();
            await bootstrap.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
            await bootstrap.Advanced.AddMartenManagedTenantsAsync(
                CancellationToken.None, tenants.Keys.ToArray());

            foreach (var (tenant, legs) in tenants)
            {
                var id = Guid.NewGuid();
                streams[tenant] = id;
                await using var session = bootstrap.LightweightSession(tenant);
                var events = new object[] { new HcStarted(id) }
                    .Concat(Enumerable.Range(0, legs).Select(_ => (object)new HcLeg(1.0)))
                    .ToArray();
                session.Events.StartStream<HcTrip>(id, events);
                await session.SaveChangesAsync();
            }
        }

        // Per-tenant event heights: 1 HcStarted + N HcLeg per tenant, independent sequences.
        var expectedHeight = tenants.ToDictionary(p => p.Key, p => (long)(p.Value + 1));
        var globalHighWater = expectedHeight.Values.Max();

        // TWO nodes in HotCold contention over the same schema + lock id.
        using var nodeA = await StartNodeAsync();
        using var nodeB = await StartNodeAsync();

        try
        {
            // Wait on progression state, not sleeps: every (projection, tenant) pair must reach
            // that tenant's OWN height — the WS1 per-tenant baseline, not the global max.
            var rows = await WaitForProgressionAsync(r =>
                    expectedHeight.All(p =>
                        SeqOf(r, $"{HcTripProjection.ProjectionName}:All:{p.Key}") >= p.Value &&
                        SeqOf(r, $"{HcTallyProjection.ProjectionName}:All:{p.Key}") >= p.Value &&
                        SeqOf(r, $"HighWaterMark:{p.Key}") >= p.Value) &&
                    SeqOf(r, "HighWaterMark") >= globalHighWater,
                45.Seconds(), "every per-tenant projection shard reaches its tenant's own height");
            DumpRows("mt_event_progression once caught up", rows);

            // ---- Placement: which node runs what ----
            var agentsA = AgentIdentities(nodeA);
            var agentsB = AgentIdentities(nodeB);
            _output.WriteLine($"node A agents: [{string.Join(", ", agentsA)}]");
            _output.WriteLine($"node B agents: [{string.Join(", ", agentsB)}]");

            var projections = new[] { HcTripProjection.ProjectionName, HcTallyProjection.ProjectionName };
            var perTenantIdentities = projections
                .SelectMany(p => tenants.Keys.Select(t => $"{p}:All:{t}"))
                .ToArray();

            // jasperfx#491: every running agent carries a tenant slot — the distributor expanded
            // the store-global names before the winning node started anything, so no store-global
            // {Projection}:All agent exists anywhere.
            agentsA.Concat(agentsB).ShouldAllBe(identity => perTenantIdentities.Contains(identity));

            // Lock granularity is unchanged: one advisory lock per STORE-GLOBAL shard, so each
            // projection's per-tenant agents run TOGETHER on exactly one node. Whether the two
            // projections split across the nodes or pile onto one is a lock race — assert
            // exclusivity + completeness per projection, not the split.
            foreach (var projection in projections)
            {
                var tenantIdentities = tenants.Keys.Select(t => $"{projection}:All:{t}").ToArray();
                var onA = agentsA.Where(tenantIdentities.Contains).ToList();
                var onB = agentsB.Where(tenantIdentities.Contains).ToList();

                var owners = new[] { onA.Any(), onB.Any() }.Count(x => x);
                owners.ShouldBe(1,
                    $"projection {projection}'s per-tenant agents must all run on exactly one node " +
                    "(they ride the projection's single store-global advisory lock)");

                var winner = onA.Any() ? onA : onB;
                winner.OrderBy(x => x).ShouldBe(tenantIdentities.OrderBy(x => x),
                    $"the node owning {projection}'s lock must run one agent per tenant");
            }

            // ---- WS1 per-tenant baseline: met on this path as of jasperfx#491 ----
            rows.Where(r => r.Name.StartsWith("Ws1Hc", StringComparison.Ordinal))
                .ShouldAllBe(r => perTenantIdentities.Contains(r.Name),
                    "per-tenant fan-out: every projection progression row is tenant-bearing — the " +
                    "store-global {Projection}:All agent never ran, so no store-global row exists");

            foreach (var (tenant, height) in expectedHeight)
            {
                SeqOf(rows, $"{HcTripProjection.ProjectionName}:All:{tenant}").ShouldBe(height,
                    $"tenant {tenant}'s trip shard tracks the tenant's OWN sequence height");
                SeqOf(rows, $"{HcTallyProjection.ProjectionName}:All:{tenant}").ShouldBe(height,
                    $"tenant {tenant}'s tally shard tracks the tenant's OWN sequence height");
                SeqOf(rows, $"HighWaterMark:{tenant}").ShouldBe(height,
                    $"tenant {tenant} is in the vectorized polled set, so its own high-water row " +
                    "is persisted at the tenant's own height");
            }

            SeqOf(rows, "HighWaterMark").ShouldBe(globalHighWater,
                "the store-global high-water detector still runs alongside the per-tenant agents " +
                "and tracks max(seq_id) across the overlapping per-tenant sequences");

            // ---- The documents themselves materialize correctly per tenant ----
            using var verify = (DocumentStore)DocumentStore.For(Configure);
            foreach (var (tenant, legs) in tenants)
            {
                await using var query = verify.QuerySession(tenant);
                var trip = await query.LoadAsync<HcTrip>(streams[tenant]);
                trip.ShouldNotBeNull($"tenant {tenant}'s HcTrip projection doc must materialize");
                trip!.Distance.ShouldBe(legs);
                var tally = await query.LoadAsync<HcTally>(streams[tenant]);
                tally.ShouldNotBeNull($"tenant {tenant}'s HcTally projection doc must materialize");
                tally!.Count.ShouldBe(legs);
            }
        }
        finally
        {
            await nodeA.StopAsync();
            await nodeB.StopAsync();
        }
    }

    private static async Task<IHost> StartNodeAsync()
    {
        return await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(Configure).AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();
    }

    private static IReadOnlyList<string> AgentIdentities(IHost node)
    {
        var coordinator = node.Services.GetRequiredService<IProjectionCoordinator>();
        return coordinator.DaemonForMainDatabase().CurrentAgents()
            .Where(x => x.Status == AgentStatus.Running)
            .Select(x => x.Name.Identity)
            .OrderBy(x => x)
            .ToList();
    }

    private async Task<List<(string Name, long Seq)>> WaitForProgressionAsync(
        Func<List<(string Name, long Seq)>, bool> condition, TimeSpan timeout, string what)
    {
        var sw = Stopwatch.StartNew();
        List<(string Name, long Seq)> rows = new();
        while (sw.Elapsed < timeout)
        {
            rows = await ProgressionRowsAsync();
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

    private static async Task<List<(string Name, long Seq)>> ProgressionRowsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            $"select name, coalesce(last_seq_id,0) from {Schema}.mt_event_progression order by name");
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, long)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }
}
