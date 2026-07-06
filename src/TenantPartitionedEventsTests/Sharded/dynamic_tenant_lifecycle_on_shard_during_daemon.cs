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
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// JasperFx/jasperfx#486 WS1 — dynamic tenant lifecycle on a SHARD database while its daemon keeps
/// running: sharded/pooled tenancy (<c>MultiTenantedWithShardedDatabases</c>) +
/// <c>UseTenantPartitionedEvents</c>, one shard daemon started for the shard's initial tenant, then
/// <c>AddTenantToShardAsync</c> assigns a NEW tenant to the SAME shard mid-run.
///
/// <para>
/// Pins, in order:
/// <list type="number">
///   <item><c>AddTenantToShardAsync</c> mid-run provisions the new tenant's partitions AND its
///     per-tenant <c>mt_events_sequence_{suffix}</c> on the target shard database immediately —
///     appends for the new tenant work right away (no MT002).</item>
///   <item>CURRENT BEHAVIOR (JasperFx.Events 2.20.0): the RUNNING shard daemon does NOT discover the
///     new tenant on its own — per-tenant fan-out (<c>buildPerTenantContinuousAgents</c>) only runs
///     inside <c>StartAllAsync</c>; the high-water poll loop only polls tenants that already have a
///     tenant-bearing agent. Same gap as the single-database flavor
///     (<c>dynamic_tenant_lifecycle_during_continuous_daemon</c>).</item>
///   <item>The no-restart path that DOES work (wolverine#3280):
///     <c>StartAgentAsync("{Projection}:All:{tenant}")</c> fans out the tenant agent on the running
///     daemon. The new tenant's <c>{Projection}:All:{tenant}</c> and <c>HighWaterMark:{tenant}</c>
///     rows then appear in the SHARD database's own <c>mt_event_progression</c> (per-shard, no
///     cross-shard coordination) and the projection catches up to the tenant's own height.</item>
/// </list>
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class dynamic_tenant_lifecycle_on_shard_during_daemon: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;
    private DocumentStore _store = null!;

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

    [Fact]
    public async Task tenant_added_to_shard_mid_run_is_provisioned_and_catches_up_without_daemon_restart()
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

            opts.Projections.Add<ShardedDaemonProjection>(ProjectionLifecycle.Async);
            opts.Schema.For<ShardedDaemonCounter>().DocumentAlias("ws1dyn_sdc");
        });

        // ---- Phase 0: one initial tenant on shard A, daemon running, caught up ----
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

        using var daemon = await _store.BuildProjectionDaemonAsync(tenant1);
        await daemon.StartAllAsync();

        await WaitForProgressionAsync(shardConnStr, rows =>
                SeqOf(rows, $"{ShardedDaemonProjection.ProjectionName}:All:{tenant1}") >= 3 &&
                SeqOf(rows, $"HighWaterMark:{tenant1}") >= 3,
            30.Seconds(), "initial tenant reaches its own height on shard A");

        // ---- Phase 1: a NEW tenant joins the SAME shard while the daemon keeps running ----
        await _store.Advanced.AddTenantToShardAsync(tenant2, shard, CancellationToken.None);

        // Pin 1: partitions + the per-tenant sequence exist on the shard database immediately.
        (await SequenceExistsAsync(shardConnStr, $"mt_events_sequence_{tenant2}")).ShouldBeTrue(
            $"AddTenantToShardAsync must provision mt_events_sequence_{tenant2} on shard {shard}");

        // ... so appending for the new tenant works right away (Quick append would raise MT002
        // against an unregistered tenant partition).
        var stream2 = Guid.NewGuid();
        await using (var session = _store.LightweightSession(tenant2))
        {
            session.Events.StartStream<ShardedDaemonCounter>(stream2,
                new ShardedDaemonEvent("t2-1"), new ShardedDaemonEvent("t2-2"));
            await session.SaveChangesAsync();
        }

        // Pin 2 — CURRENT BEHAVIOR: the running shard daemon does not pick the new tenant up on its
        // own (tenant fan-out only happens in StartAllAsync). Bounded observation window, then the
        // explicit no-restart path below. If this pin ever fails, the daemon gained mid-run tenant
        // discovery — update this test to assert the automatic pickup instead.
        await Task.Delay(5.Seconds());
        var midRows = await ProgressionRowsAsync(shardConnStr);
        DumpRows("shard A progression after adding tenant 2, before explicit agent start", midRows);
        midRows.Any(r => r.Name == $"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}")
            .ShouldBeFalse(
                "current behavior: a tenant assigned to the shard mid-run gets no per-tenant agent " +
                "until something calls StartAgentAsync for its tenant-bearing shard identity");

        // Pin 3: the wolverine#3280 per-tenant identity start — the supported way to activate a new
        // tenant on a RUNNING daemon, no restart.
        await daemon.StartAgentAsync(
            $"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}", CancellationToken.None);

        var rows = await WaitForProgressionAsync(shardConnStr, r =>
                SeqOf(r, $"{ShardedDaemonProjection.ProjectionName}:All:{tenant2}") >= 2 &&
                SeqOf(r, $"HighWaterMark:{tenant2}") >= 2,
            30.Seconds(), "new tenant's per-tenant progression + high-water rows appear on shard A");
        DumpRows("shard A progression once tenant 2 caught up", rows);

        // The new tenant's projection materialized on its shard — without restarting the daemon.
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

        await daemon.StopAllAsync();
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
