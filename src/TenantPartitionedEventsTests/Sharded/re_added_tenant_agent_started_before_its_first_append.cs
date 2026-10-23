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
using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// A tenant removed with <c>RemoveTenantFromShardAsync</c> and re-added with
/// <c>AddTenantToShardAsync</c> while a HotCold coordinator keeps running "starts fresh": a new
/// per-tenant sequence from 1, and a new agent that catches up from the tenant's new, empty baseline
/// (docs/events/multitenancy.md, #4880). The new agent can start before the tenant's first append
/// commits, because the coordinator re-expands on every leadership cycle. That ordering is a race in
/// <see cref="dynamic_tenant_lifecycle_on_shard_during_daemon" />, which failed about one run in ten
/// with "EventCount should be 3 but was 1": the daemon still held the high-water mark the removed
/// incarnation had reached (2), seeded the new agent with it, and the agent consumed (0, 2] as an empty
/// range before events 1 and 2 of the new sequence existed (JasperFx/jasperfx#1026, fixed in JasperFx
/// 2.91.0). This test waits for the new agent before appending, so the race goes the same way every
/// time.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class re_added_tenant_agent_started_before_its_first_append: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private DocumentStore _store = null!;

    private const int LockId = 48641;
    private const string Survivor = "readd_t1";
    private const string ReAdded = "readd_t2";

    private static readonly string ReAddedShard = $"{ShardedDaemonProjection.ProjectionName}:All:{ReAdded}";

    public re_added_tenant_agent_started_before_its_first_append(ShardedPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
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

    public async ValueTask DisposeAsync()
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
        opts.Projections.LeadershipPollingTime = 250;

        opts.Projections.Add<ShardedDaemonProjection>(ProjectionLifecycle.Async);
        opts.Schema.For<ShardedDaemonCounter>().DocumentAlias("readd_sdc");
    }

    [Fact]
    public async Task projects_every_event_of_the_new_sequence()
    {
        _store = (DocumentStore)DocumentStore.For(Configure);

        // Every shard gets the full schema, document tables included, before any tenant is registered, so
        // AddTenantToShardAsync adds the tenant's partition to the document tables too.
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var shard = _fixture.DbNames[0];
        var shardConnStr = _fixture.ConnectionStrings[shard];

        // The survivor keeps the shard's daemon busy while the other tenant comes and goes.
        await _store.Advanced.AddTenantToShardAsync(Survivor, shard, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync(ReAdded, shard, CancellationToken.None);
        await AppendAsync(Survivor, Guid.NewGuid(), 3);
        await AppendAsync(ReAdded, Guid.NewGuid(), 2);

        using var node = await new HostBuilder()
            .ConfigureServices(services => services.AddMarten(Configure).AddAsyncDaemon(DaemonMode.HotCold))
            .StartAsync(TestContext.Current.CancellationToken);

        try
        {
            var coordinator = node.Services.GetRequiredService<IProjectionCoordinator>();

            // The tenant's first incarnation reaches mark 2.
            await WaitForProgressionAsync(shardConnStr,
                rows => SeqOf(rows, ReAddedShard) >= 2 && SeqOf(rows, $"HighWaterMark:{ReAdded}") >= 2,
                "the tenant's first incarnation catches up to 2");

            // Removed: its partitions, its sequence and its progression rows are dropped, and the running
            // coordinator reaps its agent.
            await _store.Advanced.RemoveTenantFromShardAsync(ReAdded, CancellationToken.None);
            await WaitForAsync(async () => !await IsRunningAsync(coordinator, ReAddedShard),
                "the removed tenant's agent is reaped", coordinator);

            // Re-added: the running coordinator starts a new agent for it. Wait for that agent before the
            // tenant's first append, which is the ordering that skipped events.
            await _store.Advanced.AddTenantToShardAsync(ReAdded, shard, CancellationToken.None);
            await WaitForAsync(() => IsRunningAsync(coordinator, ReAddedShard),
                "the coordinator starts a new agent for the re-added tenant", coordinator);

            var stream = Guid.NewGuid();
            await AppendAsync(ReAdded, stream, 3);

            var rows = await WaitForProgressionAsync(shardConnStr,
                r => SeqOf(r, ReAddedShard) >= 3 && SeqOf(r, $"HighWaterMark:{ReAdded}") >= 3,
                "the re-added tenant's agent reaches the tenant's new height");
            SeqOf(rows, ReAddedShard).ShouldBe(3);

            await using var query = _store.QuerySession(ReAdded);
            var counter = await query.LoadAsync<ShardedDaemonCounter>(stream, TestContext.Current.CancellationToken);
            counter.ShouldNotBeNull();
            counter.EventCount.ShouldBe(3);
        }
        finally
        {
            await node.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task AppendAsync(string tenantId, Guid stream, int count)
    {
        await using var session = _store.LightweightSession(tenantId);
        session.Events.StartStream<ShardedDaemonCounter>(stream,
            Enumerable.Range(1, count).Select(i => (object)new ShardedDaemonEvent($"{tenantId}-{i}")).ToArray());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> IsRunningAsync(IProjectionCoordinator coordinator, string identity) =>
        (await coordinator.AllDaemonsAsync()).SelectMany(x => x.CurrentAgents())
            .Any(x => x.Name.Identity == identity && x.Status == AgentStatus.Running);

    private static async Task WaitForAsync(Func<Task<bool>> condition, string what, IProjectionCoordinator coordinator)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < 30.Seconds())
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        var agents = (await coordinator.AllDaemonsAsync()).SelectMany(x => x.CurrentAgents())
            .Select(x => $"{x.Name.Identity}={x.Status}");
        throw new TimeoutException($"Timed out waiting for: {what}. Agents: {string.Join(", ", agents)}");
    }

    private static async Task<List<(string Name, long Seq)>> WaitForProgressionAsync(string connectionString,
        Func<List<(string Name, long Seq)>, bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        var rows = new List<(string Name, long Seq)>();
        while (sw.Elapsed < 30.Seconds())
        {
            rows = await ProgressionRowsAsync(connectionString);
            if (condition(rows))
            {
                return rows;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Timed out waiting for: {what}. Progression: {string.Join(", ", rows.Select(r => $"{r.Name}={r.Seq}"))}");
    }

    private static long SeqOf(List<(string Name, long Seq)> rows, string name) =>
        rows.FirstOrDefault(r => r.Name == name).Seq;

    private static async Task<List<(string Name, long Seq)>> ProgressionRowsAsync(string connectionString)
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
