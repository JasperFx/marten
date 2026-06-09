#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// #4679 — sharded reproduction attempt + regression guard. The minimal single-DB Conjoined repro
/// (<c>Regressions/Bug_4679_catch_up_per_tenant_23505</c>) does NOT fire the 23505; the user's
/// environment is <c>MultiTenantedWithShardedDatabases</c>. The untried variable was
/// <b>multiple tenants on the SAME shard</b> — the existing sharded daemon tests put one tenant per
/// shard, so <c>catchUpPerTenantAsync</c>'s per-tenant loop never iterated a second tenant within a
/// single database. This test forces three tenants onto one shard and drives the per-shard
/// <c>CatchUpAsync</c> (what <c>ForceAllMartenDaemonActivityToCatchUpAsync</c> calls per daemon).
///
/// <para>
/// <b>Result (JasperFx.Events 2.9.0 — identical catchUpPerTenantAsync code to the user's 2.8.2):
/// still does NOT reproduce.</b> The per-tenant catch-up writes correctly <i>tenant-scoped</i>
/// progression rows (<c>Bug4679ShardedTrip:All:tA</c>, <c>:All:tB</c>, …) — never the bare
/// store-global <c>:All</c> identity the user reports colliding. So this stands as a regression
/// guard proving SingleStream per-tenant catch-up under sharding is correct; the store-global leak
/// in the user's environment comes from a projection-registration / config detail not yet matched.
/// (A MultiStream <c>RollUpByTenant :All</c> projection added here instead surfaced a separate
/// <c>23514</c> doc-table partition-routing error — adjacent to #4648, not this issue.)
/// </para>
///
/// <para>
/// <b>Composite projections (the explicit hypothesis that composite support doesn't honor the
/// per-tenant ShardName) — also does NOT reproduce.</b> A multi-stage
/// <c>CompositeProjectionFor</c> caught up per-tenant writes correctly tenant-scoped rows for both
/// the composite's own shard (<c>Bug4679Composite:All:tA</c>) AND every member stage
/// (<c>Bug4679ShardedTrip:All:tA</c>, …) — see <see cref="composite_force_catch_up_with_multiple_tenants_on_one_shard"/>.
/// The composite catch-up honors <c>ShardName.ForTenant</c>; the store-global collision still
/// requires some other config detail.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public partial class Bug_4679_sharded_catch_up_23505: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Bug_4679_sharded_catch_up_23505(ShardedPartitionedFixture fixture, ITestOutputHelper output)
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

    public Task DisposeAsync() => Task.CompletedTask;

    public class Bug4679Trip
    {
        public Guid Id { get; set; }
        public double Distance { get; set; }
        public int Version { get; set; }
    }

    public record Bug4679TripStarted(Guid Id);
    public record Bug4679TripLeg(double Distance);

    public partial class Bug4679TripProjection: SingleStreamProjection<Bug4679Trip, Guid>
    {
        public Bug4679TripProjection()
        {
            // Store-global :All shard — the shape the bug fires on.
            Name = "Bug4679ShardedTrip";
        }

        public void Apply(Bug4679Trip agg, Bug4679TripLeg @event) => agg.Distance += @event.Distance;
    }

    // Second projection to more closely match the user's "wide set of async :All projections".
    public class Bug4679Count
    {
        public Guid Id { get; set; }
        public int Count { get; set; }
        public int Version { get; set; }
    }

    public partial class Bug4679CountProjection: SingleStreamProjection<Bug4679Count, Guid>
    {
        public Bug4679CountProjection()
        {
            Name = "Bug4679ShardedCount";
        }

        public void Apply(Bug4679Count agg, Bug4679TripLeg @event) => agg.Count++;
    }

    private void configure(StoreOptions opts)
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
        opts.Events.AddEventType<Bug4679TripLeg>();

        opts.Projections.Add<Bug4679TripProjection>(ProjectionLifecycle.Async);
        opts.Projections.Add<Bug4679CountProjection>(ProjectionLifecycle.Async);
        opts.Schema.For<Bug4679Trip>().DocumentAlias("b4679_trip");
        opts.Schema.For<Bug4679Count>().DocumentAlias("b4679_cnt");
    }

    [Fact]
    public async Task force_catch_up_with_multiple_tenants_on_one_shard()
    {
        // Provision + seed via a standalone store; the host below runs the same config.
        // KEY CONDITION: 3 tenants on ONE shard (so that shard's catchUpPerTenantAsync loop
        // iterates >1 tenant for each store-global :All shard). The other shards get one tenant
        // each so every shard has its tenants.mt_tenant_partitions table (an empty shard would
        // otherwise 42P01 in the catch-up path and mask the bug under test).
        var tenants = new[] { "tA", "tB", "tC", "tD", "tE" };
        var shardAssignment = new Dictionary<string, string>
        {
            ["tA"] = _fixture.DbNames[0],
            ["tB"] = _fixture.DbNames[0],
            ["tC"] = _fixture.DbNames[0],
            ["tD"] = _fixture.DbNames[1],
            ["tE"] = _fixture.DbNames[2],
        };

        await using var store = (DocumentStore)DocumentStore.For(configure);
        foreach (var tenant in tenants)
        {
            await store.Advanced.AddTenantToShardAsync(tenant, shardAssignment[tenant], CancellationToken.None);
        }

        foreach (var tenant in tenants)
        {
            var streamId = Guid.NewGuid();
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream<Bug4679Trip>(streamId,
                new Bug4679TripStarted(streamId),
                new Bug4679TripLeg(1.0),
                new Bug4679TripLeg(2.5));
            await session.SaveChangesAsync();
        }

        // Drive the buggy path directly against the shard that carries 3 tenants. This is exactly
        // what ForceAllMartenDaemonActivityToCatchUpAsync does per-daemon (StopAll + CatchUpAsync),
        // minus the coordinator/host full-schema re-apply that otherwise masks the bug.
        using var daemon = await store.BuildProjectionDaemonAsync("tA");

        Exception? thrown = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await daemon.CatchUpAsync(cts.Token);
        }
        catch (Exception e)
        {
            thrown = e;
            _output.WriteLine(e.ToString());
        }

        // If the regression reproduced, this would throw 23505 on pk_mt_event_progression for a
        // store-global (:All) name when the per-tenant loop hits shard_a's second tenant.
        thrown.ShouldBeNull(
            "catchUpPerTenantAsync must not throw on pk_mt_event_progression for store-global " +
            "(:All) shards when multiple tenants share a shard. Captured: " + thrown?.Message);

        // Pin the correct behavior: every progression row on the multi-tenant shard is
        // TENANT-SCOPED (…:All:{tenant}); there is NO bare store-global …:All row (which is what
        // the user reports colliding). This is the guard that would flip red if the per-tenant
        // catch-up ever started writing store-global progression names.
        var names = await progressionNamesAsync(_fixture.ConnectionStrings[shardAssignment["tA"]]);
        names.ShouldContain("Bug4679ShardedTrip:All:tA");
        names.ShouldContain("Bug4679ShardedTrip:All:tB");
        names.ShouldNotContain("Bug4679ShardedTrip:All");
        names.ShouldNotContain("Bug4679ShardedCount:All");
    }

    private void configureComposite(StoreOptions opts)
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
        opts.Events.AddEventType<Bug4679TripLeg>();

        // A multi-stage COMPOSITE projection — the path the user flagged. Members live in
        // different stages; the composite owns a single store-global shard (Bug4679Composite:All).
        opts.Projections.CompositeProjectionFor("Bug4679Composite", c =>
        {
            c.Add<Bug4679TripProjection>(stageNumber: 1);
            c.Add<Bug4679CountProjection>(stageNumber: 2);
        });

        opts.Schema.For<Bug4679Trip>().DocumentAlias("b4679_trip");
        opts.Schema.For<Bug4679Count>().DocumentAlias("b4679_cnt");
    }

    [Fact]
    public async Task composite_force_catch_up_with_multiple_tenants_on_one_shard()
    {
        var tenants = new[] { "tA", "tB", "tC", "tD", "tE" };
        var shardAssignment = new Dictionary<string, string>
        {
            ["tA"] = _fixture.DbNames[0],
            ["tB"] = _fixture.DbNames[0],
            ["tC"] = _fixture.DbNames[0],
            ["tD"] = _fixture.DbNames[1],
            ["tE"] = _fixture.DbNames[2],
        };

        await using var store = (DocumentStore)DocumentStore.For(configureComposite);
        foreach (var tenant in tenants)
        {
            await store.Advanced.AddTenantToShardAsync(tenant, shardAssignment[tenant], CancellationToken.None);
        }

        foreach (var tenant in tenants)
        {
            var streamId = Guid.NewGuid();
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream<Bug4679Trip>(streamId,
                new Bug4679TripStarted(streamId),
                new Bug4679TripLeg(1.0),
                new Bug4679TripLeg(2.5));
            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync("tA");

        Exception? thrown = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await daemon.CatchUpAsync(cts.Token);
        }
        catch (Exception e)
        {
            thrown = e;
            _output.WriteLine("=== CatchUpAsync threw ===");
            _output.WriteLine(e.ToString());
        }

        var names = await progressionNamesAsync(_fixture.ConnectionStrings[shardAssignment["tA"]]);
        _output.WriteLine("=== mt_event_progression rows on shard_a ===");
        foreach (var n in names) _output.WriteLine(n);

        // OBSERVE: does the composite write store-global (tenant-less) progression names? If the
        // composite execution doesn't honor the per-tenant ShardName, these bare :All rows appear
        // (and collide on the 2nd tenant under InsertProjectionProgress => 23505).
        thrown.ShouldBeNull("composite catch-up threw — captured: " + thrown?.Message);
        names.ShouldNotContain("Bug4679Composite:All");
        names.ShouldNotContain("Bug4679ShardedTrip:All");
        names.ShouldNotContain("Bug4679ShardedCount:All");
    }

    private static async Task<List<string>> progressionNamesAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand("select name from public.mt_event_progression order by name");
        await using var reader = await cmd.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
