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
/// JasperFx/jasperfx#486 WS1 — dynamic tenant lifecycle against a CONTINUOUSLY RUNNING coordinator
/// on a single per-tenant-partitioned database (DefaultTenancy + Conjoined +
/// UseTenantPartitionedEvents), hosted via <c>AddAsyncDaemon(DaemonMode.Solo)</c> so the
/// <c>SoloProjectionDistributor</c> per-tenant expansion path (jasperfx#491) is what drives agents.
///
/// <para>
/// As of JasperFx.Events 2.21.0 (jasperfx#491, consumed with Marten #4862's broadened
/// <c>DistributesAgentsPerTenant</c> gate), the coordinator re-runs
/// <c>BuildDistributionAsync</c> every leadership polling cycle and the distributor re-expands the
/// store-global shard names from the database's CURRENT tenant registry
/// (<c>mt_tenant_partitions</c> via <c>ICrossTenantRebuildSource</c>). So:
/// <list type="bullet">
///   <item>A tenant added mid-run (<c>AddMartenManagedTenantsAsync</c>, possibly from another
///     process — here a separate client store) is discovered on the next polling cycle and its
///     per-tenant agent starts WITHOUT a restart and WITHOUT the explicit per-tenant
///     <c>StartAgentAsync</c> workaround (wolverine#3280) this test previously had to drive.</item>
///   <item>A tenant removed mid-run (<c>RemoveMartenManagedTenantsAsync</c>) falls out of the next
///     expansion and the coordinator's reconciliation pass REAPS its running agent (previously the
///     agent lingered idle forever). The #4683 cleanup deletes its progression + high-water rows
///     and nothing re-creates them.</item>
/// </list>
/// This test previously pinned the pre-#491 behavior (no mid-run discovery, lingering agent after
/// removal) and was flipped when the fix landed. LeadershipPollingTime is tuned down to 250ms so
/// convergence is fast and deterministic instead of sleeping the default window. NOTE the cadence
/// caveat documented at Phase 1: the per-tenant high-water poll currently rides the GLOBAL
/// high-water cadence, so the mid-run tenant is seeded above the store's global mark to converge
/// deterministically on either side of the discovery-vs-append race.
/// </para>
/// </summary>
public partial class dynamic_tenant_lifecycle_during_continuous_daemon
{
    private readonly ITestOutputHelper _output;

    public dynamic_tenant_lifecycle_during_continuous_daemon(ITestOutputHelper output) => _output = output;

    public class DynCounter { public Guid Id { get; set; } public int Count { get; set; } }

    public record DynStarted(Guid Id);
    public record DynBumped(string Label);

    public partial class DynLifeProjection: SingleStreamProjection<DynCounter, Guid>
    {
        public const string ProjectionName = "DynLife";
        public DynLifeProjection() => Name = ProjectionName;
        public void Apply(DynCounter c, DynBumped e) => c.Count++;
    }

    private static readonly string Schema = $"ws1_dynlife_p{Environment.ProcessId}";

    private static void Configure(StoreOptions o)
    {
        o.Connection(ConnectionSource.ConnectionString);
        o.DatabaseSchemaName = Schema;
        o.DisableNpgsqlLogging = true;
        o.Events.TenancyStyle = TenancyStyle.Conjoined;
        o.Events.UseTenantPartitionedEvents = true;
        o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        o.Policies.AllDocumentsAreMultiTenanted();

        // The dynamic add/remove below converges on the coordinator's leadership polling cycle —
        // tune it down so the test is fast and deterministic.
        o.Projections.LeadershipPollingTime = 250;

        o.Schema.For<DynCounter>().DocumentAlias("ws1_dyn_cnt");
        o.Projections.Add<DynLifeProjection>(ProjectionLifecycle.Async);
    }

    [Fact]
    public async Task tenant_added_mid_run_converges_via_coordinator_and_removal_reaps_the_agent()
    {
        // A plain client store handles seeding, tenant management, appends, and queries — the
        // daemon runs in a separate host so tenant changes made "from outside" (another process,
        // another node) are what the coordinator has to discover.
        using var store = (DocumentStore)DocumentStore.For(Configure);

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // ---- Phase 0: two initial tenants with different heights, daemon host started AFTER seeding ----
        const string tenantA = "dynlife_a";
        const string tenantB = "dynlife_b";
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenantA, tenantB);

        var streamA = await AppendStreamAsync(store, tenantA, 4); // 1 DynStarted + 4 DynBumped = 5 events
        var streamB = await AppendStreamAsync(store, tenantB, 2); // 3 events

        using var node = await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(Configure).AddAsyncDaemon(DaemonMode.Solo);
            }).StartAsync();
        var daemon = node.Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();

        try
        {
            await WaitForProgressionAsync(rows =>
                SeqOf(rows, $"{DynLifeProjection.ProjectionName}:All:{tenantA}") >= 5 &&
                SeqOf(rows, $"{DynLifeProjection.ProjectionName}:All:{tenantB}") >= 3 &&
                SeqOf(rows, $"HighWaterMark:{tenantA}") >= 5 &&
                SeqOf(rows, $"HighWaterMark:{tenantB}") >= 3,
                30.Seconds(), "initial tenants reach their own per-tenant heights");

            // jasperfx#491: the distributor expanded the store-global shard into per-tenant agents —
            // no store-global agent runs.
            var initialAgents = RunningAgents(daemon);
            _output.WriteLine("agents after start: " + string.Join(", ", initialAgents));
            initialAgents.OrderBy(x => x).ShouldBe(new[]
            {
                $"{DynLifeProjection.ProjectionName}:All:{tenantA}",
                $"{DynLifeProjection.ProjectionName}:All:{tenantB}"
            }.OrderBy(x => x));

            // ---- Phase 1: a NEW tenant joins while the coordinator keeps running ----
            // Tenant C is deliberately seeded ABOVE the store's current global high water (a is at
            // 5, so C gets 7 events in its own overlapping sequence). KNOWN CADENCE GAP (jasperfx,
            // observed against JasperFx.Events 2.21.0): the vectorized per-tenant high-water poll
            // rides the GLOBAL high-water cadence — JasperFxAsyncDaemon only calls
            // pollTenantHighWaterAsync when the store-global HighWaterMark shard state fires, which
            // under overlapping per-tenant sequences only happens when max(seq_id) over the whole
            // table moves. If the coordinator's discovery wins the race against this append (it
            // polls every 250ms here), C's agent is primed at ceiling 0, and events that stay
            // BELOW the global mark would never be routed to it — the agent stalls at 0
            // indefinitely. Seeding C past the global mark forces a global high-water change, which
            // triggers the per-tenant poll and converges C on EITHER side of the race. When the
            // cadence gap is fixed upstream (per-tenant polls on the timer, not on global-mark
            // change), C's height can go back below the global mark.
            const string tenantC = "dynlife_c";
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenantC);
            var streamC = await AppendStreamAsync(store, tenantC, 6); // 7 events > global HW of 5

            // FLIPPED (was the pre-#491 pin): the running coordinator DOES discover the new tenant
            // on its own — the next BuildDistributionAsync re-expands from mt_tenant_partitions and
            // starts tenant C's agent, which catches up to the tenant's own height. No explicit
            // per-tenant StartAgentAsync (wolverine#3280) workaround, no restart.
            await WaitForProgressionAsync(rows =>
                SeqOf(rows, $"{DynLifeProjection.ProjectionName}:All:{tenantC}") >= 7 &&
                SeqOf(rows, $"HighWaterMark:{tenantC}") >= 7,
                30.Seconds(),
                "tenant C converges via the coordinator's own re-enumeration, no explicit agent start");

            RunningAgents(daemon).ShouldContain($"{DynLifeProjection.ProjectionName}:All:{tenantC}",
                "the coordinator's polling cycle must have started the new tenant's agent");

            // And the projection itself materialized for the new tenant — without any restart.
            await using (var query = store.QuerySession(tenantC))
            {
                var doc = await query.LoadAsync<DynCounter>(streamC);
                doc.ShouldNotBeNull();
                doc.Count.ShouldBe(6);
            }

            // The original tenants were untouched by the dynamic add.
            await using (var query = store.QuerySession(tenantA))
            {
                (await query.LoadAsync<DynCounter>(streamA))!.Count.ShouldBe(4);
            }

            // ---- Phase 2: remove a tenant while the coordinator keeps running ----
            // RemoveMartenManagedTenantsAsync drops tenant B's partitions + per-tenant sequence and
            // deletes its per-tenant progression rows (#4683 cleanup).
            await store.Advanced.RemoveMartenManagedTenantsAsync(new[] { tenantB }, CancellationToken.None);

            // FLIPPED (was the pre-#491 lingering-agent pin): tenant B falls out of the next
            // expansion, and the coordinator's reconciliation pass (reapOrphanedAgentsAsync) stops
            // its agent within the polling window — removal now reaps instead of leaving the agent
            // registered idle forever.
            await WaitForConditionAsync(
                () => !daemon.CurrentAgents().Any(x =>
                    x.Name.Identity == $"{DynLifeProjection.ProjectionName}:All:{tenantB}"),
                15.Seconds(),
                "tenant B's agent is reaped by coordinator reconciliation after removal");
            _output.WriteLine("agents after removal: " +
                              string.Join(", ", daemon.CurrentAgents().Select(x => x.Name.Identity)));

            // B's progression + high-water rows are gone and STAY gone across further polling
            // cycles: the reaped agent no longer advances a progression row, and StopAgentAsync's
            // syncTenantPolling drops B from the vectorized polled set so no HighWaterMark:B row is
            // ever re-persisted.
            await Task.Delay(2.Seconds());
            var finalRows = await ProgressionRowsAsync();
            DumpRows("after removing tenant B", finalRows);
            finalRows.Any(r => r.Name == $"{DynLifeProjection.ProjectionName}:All:{tenantB}").ShouldBeFalse(
                "the #4683 cleanup deletes the removed tenant's progression rows and the reaped " +
                "agent must not re-create them");
            finalRows.Any(r => r.Name == $"HighWaterMark:{tenantB}").ShouldBeFalse(
                "the removed tenant's high-water row must not be re-persisted after removal");

            // Survivors are unaffected by the removal.
            SeqOf(finalRows, $"{DynLifeProjection.ProjectionName}:All:{tenantA}").ShouldBe(5);
            SeqOf(finalRows, $"{DynLifeProjection.ProjectionName}:All:{tenantC}").ShouldBe(7);
        }
        finally
        {
            await node.StopAsync();
        }
    }

    private static IReadOnlyList<string> RunningAgents(IProjectionDaemon daemon) =>
        daemon.CurrentAgents()
            .Where(x => x.Status == AgentStatus.Running)
            .Select(x => x.Name.Identity)
            .ToList();

    private static async Task<Guid> AppendStreamAsync(DocumentStore store, string tenant, int bumps)
    {
        var id = Guid.NewGuid();
        await using var session = store.LightweightSession(tenant);
        var events = new object[] { new DynStarted(id) }
            .Concat(Enumerable.Range(0, bumps).Select(i => (object)new DynBumped($"{tenant}-{i}")))
            .ToArray();
        session.Events.StartStream<DynCounter>(id, events);
        await session.SaveChangesAsync();
        return id;
    }

    private async Task WaitForProgressionAsync(
        Func<List<(string Name, long Seq)>, bool> condition, TimeSpan timeout, string what)
    {
        var sw = Stopwatch.StartNew();
        List<(string Name, long Seq)> rows = new();
        while (sw.Elapsed < timeout)
        {
            rows = await ProgressionRowsAsync();
            if (condition(rows))
            {
                return;
            }

            await Task.Delay(250);
        }

        DumpRows($"TIMED OUT waiting for: {what}", rows);
        await DumpEventsAsync();
        throw new TimeoutException($"Timed out after {timeout} waiting for: {what}");
    }

    private async Task DumpEventsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            $"select tenant_id, count(*), max(seq_id) from {Schema}.mt_events group by tenant_id order by tenant_id");
        await using var reader = await cmd.ExecuteReaderAsync();
        _output.WriteLine("=== mt_events by tenant ===");
        while (await reader.ReadAsync())
        {
            _output.WriteLine($"{reader.GetString(0)} | count={reader.GetInt64(1)} | max_seq={reader.GetInt64(2)}");
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, string what)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(250);
        }

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
