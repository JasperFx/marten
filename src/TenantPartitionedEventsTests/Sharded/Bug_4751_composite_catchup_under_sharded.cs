#nullable enable
using System;
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
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

public record CmpLeg(double Distance);

public class CmpTrip
{
    public Guid Id { get; set; }
    public double Distance { get; set; }
}

public class CmpTripCount
{
    public Guid Id { get; set; }
    public int Legs { get; set; }
}

public partial class CmpTripProjection: SingleStreamProjection<CmpTrip, Guid>
{
    public CmpTripProjection() => Name = "CmpTrip";
    public void Apply(CmpTrip agg, CmpLeg e) => agg.Distance += e.Distance;
}

public partial class CmpTripCountProjection: SingleStreamProjection<CmpTripCount, Guid>
{
    public CmpTripCountProjection() => Name = "CmpTripCount";
    public void Apply(CmpTripCount agg, CmpLeg _) => agg.Legs++;
}

/// <summary>
/// #4751 — under <c>MultiTenantedWithShardedDatabases</c> + <c>UseTenantPartitionedEvents</c>, an async
/// <c>CompositeProjection</c> is not driven to a non-stale state by the normal daemon catch-up path
/// (<c>StartAllAsync</c> + <c>WaitForNonStaleData</c> / <c>IDocumentStore.WaitForNonStaleProjectionDataAsync</c>).
/// The composite's member read models stay empty after catch-up returns.
///
/// <para>
/// Contrast: <see cref="composite_side_effects_rebuild_under_sharded_partitioning"/> exercises the SAME
/// shape of composite but via <c>RebuildProjectionAsync</c>, which DOES complete and materialize the
/// documents. So the composite/projection logic is fine — the gap is specifically the continuous
/// async catch-up path for a composite on a sharded, tenant-partitioned store (the per-tenant composite
/// agent churns / its caught-up check counts store-global shards, not per-tenant ones — see the note in
/// <see cref="sharded_daemon_per_shard_progression"/>).
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4751_composite_catchup_under_sharded: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;
    private DocumentStore _store = null!;

    public Bug_4751_composite_catchup_under_sharded(ShardedPartitionedFixture fixture, ITestOutputHelper output)
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
    public async Task composite_reaches_non_stale_via_normal_daemon_catchup()
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
            opts.Events.AddEventType<CmpLeg>();

            opts.Projections.CompositeProjectionFor("Cmp", c =>
            {
                c.Add<CmpTripProjection>(stageNumber: 1);
                c.Add<CmpTripCountProjection>(stageNumber: 2);
            });

            opts.Schema.For<CmpTrip>().DocumentAlias("cmp_trip");
            opts.Schema.For<CmpTripCount>().DocumentAlias("cmp_trip_count");
        });

        var shard = _fixture.DbNames[0];
        await _store.Advanced.AddTenantToShardAsync("tenant_a", shard, CancellationToken.None);

        const int streams = 10;
        await using (var session = _store.LightweightSession("tenant_a"))
        {
            for (var i = 0; i < streams; i++)
            {
                session.Events.StartStream<CmpTrip>(Guid.NewGuid(), new CmpLeg(1.0), new CmpLeg(2.5));
            }
            await session.SaveChangesAsync();
        }

        // Normal async catch-up (NOT RebuildProjectionAsync): start the daemon and wait for non-stale.
        using var daemon = await _store.BuildProjectionDaemonAsync("tenant_a");
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(20.Seconds());

        // After the catch-up helper returns, BOTH composite stages must be materialized for the tenant.
        await using var query = _store.QuerySession("tenant_a");
        var stage1 = await query.Query<CmpTrip>().CountAsync();
        var stage2 = await query.Query<CmpTripCount>().CountAsync();
        _output.WriteLine($"stage1 (CmpTrip) = {stage1}, stage2 (CmpTripCount) = {stage2}, expected {streams}");

        stage1.ShouldBe(streams, "stage-1 of the composite must be caught up by the async daemon");
        stage2.ShouldBe(streams, "stage-2 of the composite must be caught up by the async daemon");
    }
}
