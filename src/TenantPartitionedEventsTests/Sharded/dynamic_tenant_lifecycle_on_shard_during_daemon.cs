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
/// JasperFx/jasperfx#486 WS1 — dynamic tenant lifecycle on a SHARD database while a HotCold
/// coordinator node keeps running: sharded/pooled tenancy (<c>MultiTenantedWithShardedDatabases</c>)
/// + <c>UseTenantPartitionedEvents</c>, one coordinator node started for the shard's initial
/// tenant, then <c>AddTenantToShardAsync</c> assigns a NEW tenant to the SAME shard mid-run from a
/// separate client store.
///
/// <para>
/// Pins, in order:
/// <list type="number">
///   <item><c>AddTenantToShardAsync</c> mid-run provisions the new tenant's partitions AND its
///     per-tenant <c>mt_events_sequence_{suffix}</c> on the target shard database immediately —
///     appends for the new tenant work right away (no MT002).</item>
///   <item>FLIPPED (was the pre-jasperfx#491 pin): the RUNNING coordinator node DOES discover the
///     new tenant on its own. The <c>MultiTenantedProjectionDistributor</c> re-expands each shard
///     database's set from that database's own tenant registry on every leadership polling cycle
///     (jasperfx#491, consumed via Marten #4862's <c>DistributesAgentsPerTenant</c>), so the new
///     tenant's <c>{Projection}:All:{tenant}</c> and <c>HighWaterMark:{tenant}</c> rows appear in
///     the SHARD database's own <c>mt_event_progression</c> and the projection catches up to the
///     tenant's own height — WITHOUT a restart and WITHOUT the explicit per-tenant
///     <c>StartAgentAsync</c> (wolverine#3280) workaround this test previously had to drive.</item>
///   <item>#4868/#4880 — the REMOVE side of the lifecycle: <c>Advanced.RemoveTenantFromShardAsync</c>
///     mid-run (from the separate client store) shrinks the client store's usage descriptor
///     immediately (no restart), drops the tenant from the shard's own
///     <c>mt_tenant_partitions</c> registry, and the RUNNING HotCold coordinator's next
///     re-expansion stops producing the tenant's shard name — its agent is REAPED by
///     the jasperfx#491 reconciliation pass (<c>reapOrphanedAgentsAsync</c>). The tenant's
///     per-tenant progression + high-water rows are cleaned (#4683 semantics) and stay gone,
///     while the surviving tenant keeps processing new events on the same shard daemon.</item>
/// </list>
/// LeadershipPollingTime is tuned down to 250ms so the mid-run discovery converges fast and
/// deterministically instead of sleeping the default window.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class dynamic_tenant_lifecycle_on_shard_during_daemon: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;
    private DocumentStore _store = null!;

    private const int LockId = 48619;

    public dynamic_tenant_lifecycle_on_shard_during_daemon(
        ShardedPartitionedFixture fixture, ITestOutputHelper output)
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
        if (_store != null!)
        {
            await _store.DisposeAsync();
        }
    }

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
        opts.Events.AddEventType<ShardedDaemonEvent>();

        opts.Projections.DaemonLockId = LockId;
        // Mid-run tenant discovery converges on the coordinator's leadership polling cycle —
        // tune it down so the test is fast and deterministic.
        opts.Projections.LeadershipPollingTime = 250;

        opts.Projections.Add<ShardedDaemonProjection>(ProjectionLifecycle.Async);
        opts.Schema.For<ShardedDaemonCounter>().DocumentAlias("ws1dyn_sdc");
    }

    [Fact]
    public async Task tenant_added_to_shard_mid_run_is_provisioned_and_catches_up_without_daemon_restart()
    {
        _store = (DocumentStore)DocumentStore.For(Configure);

        // Materialize the FULL schema (including the projection document tables) on every shard
        // database BEFORE registering tenants, so AddTenantToShardAsync adds each tenant's list
        // partition to the doc tables too — the coordinator node below is a FRESH store whose lazy
        // table creation does not hydrate partitions from the shard's own registry (see the matrix
        // test multi_node_hotcold_sharded_partitioned_events for the 23514 details).
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // ---- Phase 0: one initial tenant on shard A, coordinator node running, caught up ----
        var shard = _fixture.DbNames[0];
        var shardConnStr = _fixture.ConnectionStrings[shard];
        const string tenant1 = "ws1dyn_t1";
        const string tenant2 = "ws1dyn_t2";

        await _store.Advanced.AddTenantToShardAsync(tenant1, shard, CancellationToken.None);

        var stream1 = Guid.NewGuid();
        await using (var session = _store.LightweightSession(tenant1))
        {
            session.Events.StartStream<ShardedDaemonCounter>(stream1,
                new ShardedDaemonEvent("t1-1"), new ShardedDaemonEvent("t1-2"),
                new ShardedDaemonEvent("t1-3"));
            await session.SaveChangesAsync();
        }

        using var node = await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(Configure).AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();

        try
        {
            await WaitForProgressionAsync(shardConnStr, rows =>
                    SeqOf(rows, $"{ShardedDaemonProjection.ProjectionName}:All:{tenant1}") >= 3 &&
                    SeqOf(rows, $"HighWaterMark:{tenant1}") >= 3,
                30.Seconds(), "initial tenant reaches its own height on shard A");

            // ---- Phase 1: a NEW tenant joins the SAME shard while the coordinator keeps running ----
            await _store.Advanced.AddTenantToShardAsync(tenant2, shard, CancellationToken.None);

            // Pin 1: partitions + the per-tenant sequence exist on the shard database immediately.
            (await SequenceExistsAsync(shardConnStr, $"mt_events_sequence_{tenant2}")).ShouldBeTrue(
                $"AddTenantToShardAsync must provision mt_events_sequence_{tenant2} on shard {shard}");

            // ... so appending for the new tenant works right away (Quick append would raise MT002
            // against an unregistered tenant partition). Tenant 2 is deliberately seeded BELOW the
            // shard's current global high water (tenant 1 is at 3, tenant 2 gets only 2 events in
            // its own overlapping sequence) — pinning the jasperfx#492 fix (2.21.1): the per-tenant
            // high-water poll now runs on the daemon's own timer cadence, so tenant 2 converges
            // even though its appends never move max(seq_id) over the shard's whole events table.
            // See the single-database flavor (dynamic_tenant_lifecycle_during_continuous_daemon)
            // for the full history of the cadence gap this used to work around.
            var stream2 = Guid.NewGuid();
            await using (var session = _store.LightweightSession(tenant2))
            {
                session.Events.StartStream<ShardedDaemonCounter>(stream2,
                    new ShardedDaemonEvent("t2-1"), new ShardedDaemonEvent("t2-2"));
                await session.SaveChangesAsync();
            }

            // Pin 2 — FLIPPED (was the pre-#491 negative pin + explicit StartAgentAsync): the
            // running coordinator discovers the new tenant on its next leadership polling cycle —
            // the shard database's set re-expands from its own tenants.mt_tenant_partitions
            // registry and tenant 2's agent starts and catches up. No StartAgentAsync, no restart.
            var rows = await WaitForProgressionAsync(shardConnStr, r =>
                    SeqOf(r, $"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}") >= 2 &&
                    SeqOf(r, $"HighWaterMark:{tenant2}") >= 2,
                30.Seconds(),
                "new tenant's per-tenant progression + high-water rows appear on shard A via the " +
                "coordinator's own re-enumeration");
            DumpRows("shard A progression once tenant 2 caught up", rows);

            // The tenant-bearing agent really is running on the coordinator node's shard daemon.
            var coordinator = node.Services.GetRequiredService<IProjectionCoordinator>();
            var daemon = await coordinator.DaemonForDatabase(shard);
            var agents = daemon.CurrentAgents()
                .Where(x => x.Status == AgentStatus.Running)
                .Select(x => x.Name.Identity)
                .OrderBy(x => x)
                .ToList();
            _output.WriteLine($"shard A agents: [{string.Join(", ", agents)}]");
            agents.ShouldContain($"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}",
                "the coordinator's polling cycle must have started the new tenant's agent");

            // The new tenant's projection materialized on its shard — without restarting anything.
            ShardedDaemonCounter? doc2 = null;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < 20.Seconds())
            {
                await using var query = _store.QuerySession(tenant2);
                doc2 = await query.LoadAsync<ShardedDaemonCounter>(stream2);
                if (doc2 is { EventCount: 2 })
                {
                    break;
                }

                await Task.Delay(250);
            }

            doc2.ShouldNotBeNull("tenant 2's projection doc must materialize on shard A");
            doc2!.EventCount.ShouldBe(2);

            // And the original tenant is untouched by the dynamic add — still at its own height.
            SeqOf(rows, $"{ShardedDaemonProjection.ProjectionName}:All:{tenant1}").ShouldBe(3);
            await using (var query = _store.QuerySession(tenant1))
            {
                (await query.LoadAsync<ShardedDaemonCounter>(stream1))!.EventCount.ShouldBe(3);
            }

            // ---- Phase 2 (#4868 / #4880): REMOVE tenant 2 while the coordinator keeps running ----
            await _store.Advanced.RemoveTenantFromShardAsync(tenant2, CancellationToken.None);

            // #4868: the client store's own usage descriptor shrinks IMMEDIATELY — no restart.
            // This is the signal Wolverine-managed distribution diffs per cycle to retire the
            // tenant's agents (the descriptor used to be add-only and never shrank).
            var clientUsage = await _store.Options.Tenancy.DescribeDatabasesAsync(CancellationToken.None);
            clientUsage.Databases.SelectMany(x => x.TenantIds).ShouldNotContain(tenant2);
            clientUsage.Databases.SelectMany(x => x.TenantIds).ShouldContain(tenant1);

            // ... and so does a FRESH DescribeDatabasesAsync on the RUNNING coordinator node's
            // store — the fresh-read reconciliation in BuildDatabases, i.e. the cross-node path.
            var nodeStore = node.Services.GetRequiredService<IDocumentStore>();
            var nodeUsage = await nodeStore.Options.Tenancy.DescribeDatabasesAsync(CancellationToken.None);
            nodeUsage.Databases.SelectMany(x => x.TenantIds).ShouldNotContain(tenant2);

            // #4880: the NATIVE HotCold coordinator retires the removed tenant's agent on its
            // own — the shard's mt_tenant_partitions registry no longer lists tenant 2, the
            // next leadership-cycle re-expansion (jasperfx#491's PerTenantShardExpansion) stops
            // producing its shard name, and reapOrphanedAgentsAsync stops the running agent.
            await WaitForConditionAsync(
                () => daemon.CurrentAgents().All(x =>
                    x.Name.Identity != $"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}"),
                30.Seconds(),
                "tenant 2's agent is reaped by coordinator reconciliation after removal");
            _output.WriteLine("shard A agents after removal: " +
                              string.Join(", ", daemon.CurrentAgents().Select(x => x.Name.Identity)));

            // Progression-row contract on REMOVE (pinned by #4880): the tenant's per-tenant
            // progression + high-water rows are CLEANED (#4683 semantics), and nothing
            // re-creates them once the agent is reaped — a re-added tenant starts fresh.
            var afterRemoval = await ProgressionRowsAsync(shardConnStr);
            DumpRows("shard A progression after removing tenant 2", afterRemoval);
            afterRemoval.Any(r => r.Name == $"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}")
                .ShouldBeFalse("the removed tenant's projection progression rows must be cleaned");
            afterRemoval.Any(r => r.Name == $"HighWaterMark:{tenant2}")
                .ShouldBeFalse("the removed tenant's high-water row must be cleaned");

            // The surviving tenant keeps processing NEW events on the same shard daemon —
            // the reap didn't take the shard's other agents down with it.
            await using (var session = _store.LightweightSession(tenant1))
            {
                session.Events.Append(stream1, new ShardedDaemonEvent("t1-4"));
                await session.SaveChangesAsync();
            }

            var survivorRows = await WaitForProgressionAsync(shardConnStr, r =>
                    SeqOf(r, $"{ShardedDaemonProjection.ProjectionName}:All:{tenant1}") >= 4,
                30.Seconds(), "the surviving tenant's agent keeps catching up after the removal");

            // ... and the removed tenant's rows never came back across those polling cycles.
            survivorRows.Any(r => r.Name.EndsWith($":{tenant2}", StringComparison.Ordinal))
                .ShouldBeFalse("no per-tenant progression rows may be re-persisted for the removed tenant");

            // ---- Phase 3 (#4880): RE-ADD tenant 2 after a destructive removal — the running
            // coordinator gives it a FRESH agent that catches up from a fresh per-tenant sequence,
            // pinning the "a re-added tenant starts fresh" half of the removal contract at the
            // daemon level (the registry-level version lives in
            // MultiTenancyTests/sharded_tenancy_remove_lifecycle_tests). ----
            await _store.Advanced.AddTenantToShardAsync(tenant2, shard, CancellationToken.None);

            // Re-add re-provisions the per-tenant sequence fresh (the old one was dropped on remove).
            (await SequenceExistsAsync(shardConnStr, $"mt_events_sequence_{tenant2}")).ShouldBeTrue(
                $"re-adding tenant 2 must re-provision mt_events_sequence_{tenant2} on shard {shard}");

            var reStream2 = Guid.NewGuid();
            await using (var session = _store.LightweightSession(tenant2))
            {
                session.Events.StartStream<ShardedDaemonCounter>(reStream2,
                    new ShardedDaemonEvent("t2b-1"), new ShardedDaemonEvent("t2b-2"),
                    new ShardedDaemonEvent("t2b-3"));
                await session.SaveChangesAsync();
            }

            // The running coordinator re-discovers the re-added tenant on its next leadership cycle:
            // a fresh {Projection}:All:tenant2 agent starts and catches up to the tenant's own height.
            var readdedRows = await WaitForProgressionAsync(shardConnStr, r =>
                    SeqOf(r, $"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}") >= 3 &&
                    SeqOf(r, $"HighWaterMark:{tenant2}") >= 3,
                30.Seconds(),
                "the re-added tenant gets a FRESH agent + progression on the running coordinator");
            DumpRows("shard A progression after re-adding tenant 2", readdedRows);

            // Fresh start: the destructive remove dropped tenant 2's partitions, so the pre-removal
            // projection doc is gone and only the 3 newly-appended events are projected.
            await using (var query = _store.QuerySession(tenant2))
            {
                var reDoc = await query.LoadAsync<ShardedDaemonCounter>(reStream2);
                reDoc.ShouldNotBeNull("the re-added tenant's projection doc must materialize on shard A");
                reDoc!.EventCount.ShouldBe(3);

                (await query.LoadAsync<ShardedDaemonCounter>(stream2))
                    .ShouldBeNull("the pre-removal projection doc must not survive a destructive remove + re-add");
            }

            // The re-added tenant's agent is running again on the same shard daemon.
            daemon.CurrentAgents().Select(x => x.Name.Identity)
                .ShouldContain($"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}",
                    "the coordinator must start a fresh agent for the re-added tenant");
        }
        finally
        {
            await node.StopAsync();
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

    private static async Task<bool> SequenceExistsAsync(string connectionString, string sequenceName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            "select count(*) from pg_sequences where sequencename = :name");
        cmd.Parameters.AddWithValue("name", sequenceName);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
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

    /// <summary>
    /// Each shard database hosts its own event store in its public schema (the sharded model puts
    /// mt_events / mt_event_progression there when the doc-side schema is "sharded"/"tenants").
    /// </summary>
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
