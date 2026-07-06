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
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// JasperFx/jasperfx#486 WS1 — dynamic tenant lifecycle against a CONTINUOUSLY RUNNING daemon on a
/// single per-tenant-partitioned database (DefaultTenancy + Conjoined + UseTenantPartitionedEvents).
///
/// <para>
/// The WS1 baseline for a tenant that joins mid-run is: its partitions + per-tenant sequence are
/// provisioned by <c>AddMartenManagedTenantsAsync</c>, and (once an agent addresses the tenant) a
/// per-tenant <c>{Projection}:All:{tenant}</c> progression row and a <c>HighWaterMark:{tenant}</c>
/// row appear and reach the tenant's own height — all WITHOUT restarting the daemon.
/// </para>
///
/// <para>
/// REALITY PINNED BY THIS TEST (JasperFx.Events 2.20.0): the daemon enumerates tenants for the
/// per-tenant continuous fan-out ONLY inside <c>StartAllAsync</c>
/// (<c>JasperFxAsyncDaemon.buildPerTenantContinuousAgents</c> → <c>ICrossTenantRebuildSource.FindRebuildTenantsAsync</c>).
/// There is no mid-run tenant-discovery loop, so a tenant added after start is NOT picked up
/// automatically. The mechanism that DOES work without a restart is the wolverine#3280 path:
/// <c>StartAgentAsync("{Projection}:All:{tenant}")</c> resolves the base shard, activates the tenant
/// in the vectorized high-water coordinator, and fans out the tenant agent — that is what an external
/// distributor (e.g. Wolverine) is expected to call, and what this test drives explicitly.
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

    [Fact]
    public async Task tenant_added_mid_run_catches_up_via_per_tenant_agent_start_and_removal_stops_tracking()
    {
        using var store = (DocumentStore)DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = Schema;
            o.DisableNpgsqlLogging = true;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();

            o.Schema.For<DynCounter>().DocumentAlias("ws1_dyn_cnt");
            o.Projections.Add<DynLifeProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // ---- Phase 0: two initial tenants with different heights, daemon started AFTER seeding ----
        const string tenantA = "dynlife_a";
        const string tenantB = "dynlife_b";
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenantA, tenantB);

        var streamA = await AppendStreamAsync(store, tenantA, 4); // 1 DynStarted + 4 DynBumped = 5 events
        var streamB = await AppendStreamAsync(store, tenantB, 2); // 3 events

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await WaitForProgressionAsync(rows =>
            SeqOf(rows, $"{DynLifeProjection.ProjectionName}:All:{tenantA}") >= 5 &&
            SeqOf(rows, $"{DynLifeProjection.ProjectionName}:All:{tenantB}") >= 3 &&
            SeqOf(rows, $"HighWaterMark:{tenantA}") >= 5 &&
            SeqOf(rows, $"HighWaterMark:{tenantB}") >= 3,
            30.Seconds(), "initial tenants reach their own per-tenant heights");

        // ---- Phase 1: a NEW tenant joins while the daemon keeps running ----
        const string tenantC = "dynlife_c";
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenantC);
        var streamC = await AppendStreamAsync(store, tenantC, 3); // 4 events

        // PINNED CURRENT BEHAVIOR: the running daemon does NOT discover the new tenant on its own.
        // buildPerTenantContinuousAgents runs only inside StartAllAsync; the high-water poll loop
        // (TenantedHighWaterCoordinator.PollAndRouteAsync) only polls tenants that already have a
        // tenant-bearing agent on this node. Give it a generous-but-bounded window to prove the
        // negative, then drive the supported no-restart path below. If a product change ever makes
        // this pick up automatically, this assertion should be flipped to celebrate it.
        await Task.Delay(5.Seconds());
        var midRows = await ProgressionRowsAsync();
        DumpRows("after adding tenant C, before explicit agent start", midRows);
        midRows.Any(r => r.Name == $"{DynLifeProjection.ProjectionName}:All:{tenantC}").ShouldBeFalse(
            "current behavior: a tenant added mid-run gets no per-tenant agent (tenant fan-out happens " +
            "only in StartAllAsync) — if this now passes automatically, the daemon gained mid-run tenant " +
            "discovery and this pin should be updated");

        // The no-restart mechanism that DOES exist (wolverine#3280): explicitly start the per-tenant
        // agent by its tenant-bearing identity. The daemon resolves the base shard, activates the
        // tenant in the vectorized high-water monitor, and the new agent catches up from scratch.
        await daemon.StartAgentAsync($"{DynLifeProjection.ProjectionName}:All:{tenantC}",
            CancellationToken.None);

        await WaitForProgressionAsync(rows =>
            SeqOf(rows, $"{DynLifeProjection.ProjectionName}:All:{tenantC}") >= 4 &&
            SeqOf(rows, $"HighWaterMark:{tenantC}") >= 4,
            30.Seconds(), "tenant C catches up after an explicit per-tenant agent start, no restart");

        // And the projection itself materialized for the new tenant — without any daemon restart.
        await using (var query = store.QuerySession(tenantC))
        {
            var doc = await query.LoadAsync<DynCounter>(streamC);
            doc.ShouldNotBeNull();
            doc.Count.ShouldBe(3);
        }

        // The original tenants were untouched by the dynamic add.
        await using (var query = store.QuerySession(tenantA))
        {
            (await query.LoadAsync<DynCounter>(streamA))!.Count.ShouldBe(4);
        }

        // ---- Phase 2: remove a tenant while the daemon keeps running ----
        // RemoveMartenManagedTenantsAsync drops tenant B's partitions + per-tenant sequence and
        // deletes its per-tenant progression rows (#4683 cleanup).
        await store.Advanced.RemoveMartenManagedTenantsAsync(new[] { tenantB }, CancellationToken.None);

        // PINNED CURRENT BEHAVIOR: removal does NOT stop the tenant's running agent — nothing in the
        // remove path talks to the daemon. The agent stays registered (idle: its partitions are gone,
        // so no events ever arrive again) and the polled-tenant set still contains B, but the #4683
        // cleanup removed B's progression + high-water rows and nothing re-creates them: the
        // vectorized detector no longer finds B's sequence, so no reading with CurrentMark > 0 is
        // ever produced for B again (TenantedHighWaterCoordinator only persists marks > 0).
        var agentsAfterRemoval = daemon.CurrentAgents().Select(x => x.Name.Identity).ToList();
        _output.WriteLine("agents after removal: " + string.Join(", ", agentsAfterRemoval));
        agentsAfterRemoval.ShouldContain($"{DynLifeProjection.ProjectionName}:All:{tenantB}",
            "current behavior: removing a tenant does not stop its agent — it lingers idle; " +
            "if this fails, tenant removal now reaps agents and this pin should be updated");

        // B's progression + high-water rows are gone and STAY gone across further high-water polls.
        await Task.Delay(2.Seconds());
        var finalRows = await ProgressionRowsAsync();
        DumpRows("after removing tenant B", finalRows);
        finalRows.Any(r => r.Name == $"{DynLifeProjection.ProjectionName}:All:{tenantB}").ShouldBeFalse(
            "the #4683 cleanup deletes the removed tenant's progression rows and the lingering agent " +
            "must not re-create them");
        finalRows.Any(r => r.Name == $"HighWaterMark:{tenantB}").ShouldBeFalse(
            "the removed tenant's high-water row must not be re-persisted after removal");

        // Survivors are unaffected by the removal.
        SeqOf(finalRows, $"{DynLifeProjection.ProjectionName}:All:{tenantA}").ShouldBe(5);
        SeqOf(finalRows, $"{DynLifeProjection.ProjectionName}:All:{tenantC}").ShouldBe(4);

        await daemon.StopAllAsync();
    }

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
