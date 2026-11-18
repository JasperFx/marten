#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// #6004 under <c>UseTenantPartitionedEvents</c>: every tenant draws its own sequence, so a position is
/// (tenant, sequence) and the wait reads that tenant's progression row. Another tenant's row being further along
/// says nothing about this one.
/// </summary>
public class read_your_writes_under_partitioning: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_ryw";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
        opts.Schema.For<TripDistance>().Identity(x => x.Id).DocumentAlias("ryw_trip_dist");
    }

    private async Task<EventStorePosition> appendAsync(string tenantId, int legs)
    {
        await using var session = Store.LightweightSession(tenantId);
        var id = Guid.NewGuid();
        session.Events.StartStream<TripDistance>(id,
            new object[] { new TripStarted(id) }.Concat(Enumerable.Range(0, legs).Select(_ => new TripLeg(1))).ToArray());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        return session.LastAppendedPosition.ShouldNotBeNull();
    }

    private async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private async Task<string> progressionAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand($"select name, last_seq_id from {Schema}.mt_event_progression order by name", conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add($"{reader.GetString(0)}={reader.GetInt64(1)}");
        }

        return string.Join(", ", rows);
    }

    [Fact]
    public async Task the_position_is_the_tenants_own_sequence()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "ryw_a", "ryw_b");

        var a = await appendAsync("ryw_a", 5);
        var b = await appendAsync("ryw_b", 1);

        a.TenantId.ShouldBe("ryw_a");
        a.Sequence.ShouldBe(await scalarAsync($"select max(seq_id) from {Schema}.mt_events where tenant_id = 'ryw_a'"));

        b.TenantId.ShouldBe("ryw_b");
        b.Sequence.ShouldBe(await scalarAsync($"select max(seq_id) from {Schema}.mt_events where tenant_id = 'ryw_b'"));
    }

    [Fact]
    public async Task another_tenant_being_further_along_does_not_count()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "ryw_a", "ryw_b");

        var a = await appendAsync("ryw_a", 10);
        var b = await appendAsync("ryw_b", 1);

        // Tenant a's agent is well past b's sequence number; b's agent has not run
        await scalarAsync(
            $"insert into {Schema}.mt_event_progression (name, last_seq_id, last_updated) values ('{TripDistanceProjection.ProjectionName}:All:ryw_a', {a.Sequence}, transaction_timestamp()) returning last_seq_id");
        a.Sequence.ShouldBeGreaterThan(b.Sequence);

        var forA = await Store.WaitForProjectionsAsync(a, [typeof(TripDistance)], 5.Seconds(),
            TestContext.Current.CancellationToken);
        forA.Reached.ShouldBeTrue(forA.ToString());
        forA.Shards.Single().Shard.Identity.ShouldBe($"{TripDistanceProjection.ProjectionName}:All:ryw_a");

        var forB = await Store.WaitForProjectionsAsync(b, [typeof(TripDistance)], 300.Milliseconds(),
            TestContext.Current.CancellationToken);
        forB.TimedOut.ShouldBeTrue(forB.ToString());
        forB.Shards.Single().Shard.Identity.ShouldBe($"{TripDistanceProjection.ProjectionName}:All:ryw_b");
        forB.Shards.Single().Sequence.ShouldBeNull();
    }

    [Fact]
    public async Task reached_for_each_tenant_once_the_daemon_runs()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "ryw_a", "ryw_b");

        var a = await appendAsync("ryw_a", 4);
        var b = await appendAsync("ryw_b", 2);

        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var forA = await Store.WaitForProjectionsAsync(a, [typeof(TripDistance)], 30.Seconds(),
            TestContext.Current.CancellationToken);
        forA.Reached.ShouldBeTrue($"{forA} / rows: {await progressionAsync()}");
        forA.Shards.Single().Shard.Identity.ShouldBe($"{TripDistanceProjection.ProjectionName}:All:ryw_a", $"{forA} / rows: {await progressionAsync()}");

        var forB = await Store.WaitForProjectionsAsync(b, [typeof(TripDistance)], 30.Seconds(),
            TestContext.Current.CancellationToken);
        forB.Reached.ShouldBeTrue($"{forB} / rows: {await progressionAsync()}");

        await using var session = Store.QuerySession("ryw_b");
        (await session.Query<TripDistance>().SingleAsync(TestContext.Current.CancellationToken)).Distance.ShouldBe(2);
    }
}
