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
/// DISTRIBUTION GRANULARITY OBSERVED AND PINNED HERE (JasperFx.Events 2.20.0 + Marten master):
/// with DefaultTenancy the coordinator builds a <c>SingleTenantProjectionDistributor</c> — one
/// advisory-lock-guarded <c>IProjectionSet</c> per STORE-GLOBAL shard name
/// (<c>{Projection}:All</c>, no tenant slot). The set's names are what the winning node hands to
/// <c>daemon.StartAgentAsync(identity)</c>, and that exact-identity path builds the STORE-GLOBAL
/// agent — the per-tenant fan-out (<c>buildPerTenantContinuousAgents</c>) only runs inside
/// <c>StartAllAsync</c>, which the native coordinator never calls. So:
/// <list type="bullet">
///   <item>Placement granularity = whole projection: each <c>{Projection}:All</c> shard runs on
///     exactly one node; different projections may land on different nodes. There is NO per-tenant
///     spreading, and the winning node does NOT fan out per-tenant agents either.</item>
///   <item>Progression is tracked by STORE-GLOBAL <c>{Projection}:All</c> rows against the global
///     high-water mark. The WS1 per-tenant baseline (<c>{Projection}:All:{tenant}</c> +
///     <c>HighWaterMark:{tenant}</c> rows) is NOT met under the native HotCold coordinator — that
///     per-tenant shape today only comes from <c>StartAllAsync</c> (Solo/direct daemon, see
///     Bug_4717_per_tenant_progression) or explicit per-tenant <c>StartAgentAsync</c> (Wolverine
///     distribution, wolverine#3280). Closing that gap is the point of epic #486 WS1; when the
///     product catches up, flip the per-tenant assertions below from absent to present.</item>
/// </list>
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
    public async Task two_hotcold_nodes_split_store_global_shards_without_per_tenant_fan_out()
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
            // Wait on progression state, not sleeps: both store-global shards must reach the GLOBAL
            // high-water mark (max over the overlapping per-tenant sequences: 6 here).
            var rows = await WaitForProgressionAsync(r =>
                    SeqOf(r, $"{HcTripProjection.ProjectionName}:All") >= globalHighWater &&
                    SeqOf(r, $"{HcTallyProjection.ProjectionName}:All") >= globalHighWater,
                45.Seconds(), "both store-global projection shards reach the global high water");
            DumpRows("mt_event_progression once caught up", rows);

            // ---- Placement: which node runs what ----
            var agentsA = AgentIdentities(nodeA);
            var agentsB = AgentIdentities(nodeB);
            _output.WriteLine($"node A agents: [{string.Join(", ", agentsA)}]");
            _output.WriteLine($"node B agents: [{string.Join(", ", agentsB)}]");

            var allShardIdentities = new[]
            {
                $"{HcTripProjection.ProjectionName}:All", $"{HcTallyProjection.ProjectionName}:All"
            };

            // Every running agent is a STORE-GLOBAL shard — no agent carries a tenant slot. This is
            // the observed distribution granularity: SingleTenantProjectionDistributor locks per
            // store-global shard and the winner runs the store-global agent; nobody fans out
            // per-tenant agents on this path.
            agentsA.Concat(agentsB).ShouldAllBe(identity => allShardIdentities.Contains(identity));

            // Each shard runs on EXACTLY one node (advisory lock exclusivity), and both shards run
            // somewhere. Whether they split across the nodes or pile onto one is a lock race —
            // don't assert the split, just exclusivity + coverage.
            foreach (var identity in allShardIdentities)
            {
                var owners = new[] { agentsA.Contains(identity), agentsB.Contains(identity) }
                    .Count(x => x);
                owners.ShouldBe(1,
                    $"shard {identity} must be running on exactly one of the two HotCold nodes");
            }

            // ---- WS1 per-tenant baseline: NOT met on this path today (see class doc) ----
            rows.Where(r => r.Name.StartsWith("Ws1Hc", StringComparison.Ordinal))
                .ShouldAllBe(r => allShardIdentities.Contains(r.Name),
                    "current behavior: only store-global {Projection}:All progression rows exist " +
                    "under the native HotCold coordinator — no {Projection}:All:{tenant} rows. If " +
                    "per-tenant rows now appear, native HotCold gained per-tenant fan-out and this " +
                    "test should be rewritten to assert the WS1 per-tenant baseline");
            rows.Any(r => r.Name.StartsWith("HighWaterMark:", StringComparison.Ordinal)).ShouldBeFalse(
                "current behavior: no per-tenant HighWaterMark:{tenant} rows are persisted because " +
                "no tenant ever enters the vectorized polled set on this path");
            SeqOf(rows, "HighWaterMark").ShouldBe(globalHighWater,
                "the store-global high-water row tracks max(seq_id) across the overlapping " +
                "per-tenant sequences");

            // ---- The documents themselves DO materialize correctly at this scale ----
            // The store-global agent loads pages across all tenant partitions and the conjoined
            // slicer groups by (tenant, stream), so with all events inside one page every tenant's
            // aggregate is right. (The per-tenant progression gap bites on restart/catch-up
            // semantics, not on this happy path.)
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
