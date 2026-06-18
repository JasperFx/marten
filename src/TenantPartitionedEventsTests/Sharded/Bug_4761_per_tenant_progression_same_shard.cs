#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// #4761 — under <c>MultiTenantedWithShardedDatabases</c> + <c>UseTenantPartitionedEvents</c>, when
/// MULTIPLE tenants are assigned to the SAME shard database, the async daemon does not create
/// per-tenant progression rows (<c>HighWaterMark:&lt;tenant&gt;</c> / <c>&lt;projection&gt;:All:&lt;tenant&gt;</c>).
/// Only the store-global <c>HighWaterMark</c> / <c>&lt;projection&gt;:All</c> rows exist.
///
/// <para>
/// Each tenant has its own independent <c>mt_events_sequence_&lt;suffix&gt;</c>, so their seq_id values
/// overlap. A single store-global high-water over two overlapping sequences cannot track a lagging
/// tenant independently: once the shard high-water advances past tenant Y's range (because tenant X
/// has more events), tenant Y's later appends land below the high-water and the daemon skips them.
/// #4717 added per-tenant progression to fix exactly this; this test pins that the per-tenant rows
/// are actually created when two tenants share a shard.
/// </para>
///
/// <para>
/// Distinct from <see cref="sharded_daemon_per_shard_progression"/>, which deliberately puts each
/// tenant on a DIFFERENT shard (one tenant per shard DB → a store-global row per shard is enough).
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4761_per_tenant_progression_same_shard: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;
    private DocumentStore _store = null!;

    public Bug_4761_per_tenant_progression_same_shard(ShardedPartitionedFixture fixture, ITestOutputHelper output)
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
        if (_store != null!) await _store.DisposeAsync();
    }

    [Fact]
    public async Task per_tenant_progression_rows_exist_when_two_tenants_share_a_shard()
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
            opts.Schema.For<ShardedDaemonCounter>().DocumentAlias("p4761_sdc");
        });

        // Both tenants on the SAME shard, with DIFFERENT event counts so their per-tenant
        // sequences diverge (x: 3, y: 2) and their seq_id ranges overlap.
        var shard = _fixture.DbNames[0];
        await _store.Advanced.AddTenantToShardAsync("tenant_x", shard, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("tenant_y", shard, CancellationToken.None);

        var xStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("tenant_x"))
        {
            session.Events.StartStream<ShardedDaemonCounter>(xStream,
                new ShardedDaemonEvent("x-1"), new ShardedDaemonEvent("x-2"), new ShardedDaemonEvent("x-3"));
            await session.SaveChangesAsync();
        }

        var yStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("tenant_y"))
        {
            session.Events.StartStream<ShardedDaemonCounter>(yStream,
                new ShardedDaemonEvent("y-1"), new ShardedDaemonEvent("y-2"));
            await session.SaveChangesAsync();
        }

        using var daemon = await _store.BuildProjectionDaemonAsync("tenant_x");
        await daemon.StartAllAsync();

        // Prove the projection docs DO materialize for both tenants — the per-tenant events ARE
        // processed (so this is NOT a "data not projected" problem).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ShardedDaemonCounter? dx = null, dy = null;
        while (sw.Elapsed < 20.Seconds())
        {
            await using (var q = _store.QuerySession("tenant_x")) dx = await q.LoadAsync<ShardedDaemonCounter>(xStream);
            await using (var q = _store.QuerySession("tenant_y")) dy = await q.LoadAsync<ShardedDaemonCounter>(yStream);
            if (dx is { EventCount: 3 } && dy is { EventCount: 2 }) break;
            await Task.Delay(250);
        }
        dx.ShouldNotBeNull(); dx!.EventCount.ShouldBe(3);
        dy.ShouldNotBeNull(); dy!.EventCount.ShouldBe(2);

        // The bug: a normal daemon catch-up must complete for a multi-tenant shard whose data is
        // fully projected (proven above — both tenants' docs are materialized). It instead TIMES OUT.
        // Per-tenant progression rows (HighWaterMark:<tenant>, <projection>:All:<tenant>) advance
        // correctly, but the store-global <projection>:All mark stays at 0, and WaitForNonStaleData's
        // caught-up check reads the store-global mark — so it never sees the shard as non-stale.
        await daemon.WaitForNonStaleData(20.Seconds());
    }
}
