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
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// Coverage for the full VIPLive zorgdeclaraties production configuration that surfaced the
/// composite-rebuild deadlock (marten#4727): <c>MultiTenantedWithShardedDatabases</c> +
/// <c>TenancyStyle.Conjoined</c> + <c>UseTenantPartitionedEvents</c> + a multi-stage
/// <c>CompositeProjectionFor</c> whose stage-2 member PUBLISHES side-effect messages
/// (<c>RaiseSideEffects</c> -&gt; <c>slice.PublishMessage(...)</c>), driven through an optimized
/// composite rebuild.
///
/// The optimized composite rebuild runs in <c>ShardExecutionMode.Continuous</c>, so stage-2 side
/// effects fire and the parallel event slices all call
/// <c>ProjectionUpdateBatch.CurrentMessageBatch</c> concurrently. Before the #4727 fix that leaked
/// the batch semaphore and the rebuild deadlocked forever; this test pins that the rebuild
/// completes and every tenant's documents on the multi-tenant shard materialize.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public partial class composite_side_effects_rebuild_under_sharded_partitioning: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly RecordingOutbox _outbox = new();

    public composite_side_effects_rebuild_under_sharded_partitioning(ShardedPartitionedFixture fixture, ITestOutputHelper output)
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

    public record TripStarted(Guid Id);
    public record TripLeg(double Distance);

    public class CsTrip
    {
        public Guid Id { get; set; }
        public double Distance { get; set; }
        public int Version { get; set; }
    }

    public partial class CsTripProjection: SingleStreamProjection<CsTrip, Guid>
    {
        public CsTripProjection() => Name = "CsTrip";
        public void Apply(CsTrip agg, TripLeg e) => agg.Distance += e.Distance;
    }

    public class CsTripNotice
    {
        public Guid Id { get; set; }
        public int Legs { get; set; }
        public int Version { get; set; }
    }

    public record TripNoticed(Guid Id);

    /// <summary>Stage-2 member that publishes a side-effect message for every slice — the
    /// concurrency that contends on the shared ProjectionUpdateBatch message-batch semaphore.</summary>
    public partial class CsTripNoticeProjection: SingleStreamProjection<CsTripNotice, Guid>
    {
        public CsTripNoticeProjection() => Name = "CsTripNotice";
        public void Apply(CsTripNotice agg, TripLeg e) => agg.Legs++;

        public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<CsTripNotice> slice)
        {
            slice.PublishMessage(new TripNoticed(slice.Events().First().StreamId));
            return new ValueTask();
        }
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
        opts.Events.AddEventType<TripLeg>();

        // The side-effect path goes through this outbox; a no-op recorder keeps the test focused
        // on the rebuild completing (not on message delivery).
        opts.Events.MessageOutbox = _outbox;

        opts.Projections.CompositeProjectionFor("CsComposite", c =>
        {
            c.Add<CsTripProjection>(stageNumber: 1);
            c.Add<CsTripNoticeProjection>(stageNumber: 2);
        });

        opts.Schema.For<CsTrip>().DocumentAlias("cs_trip");
        opts.Schema.For<CsTripNotice>().DocumentAlias("cs_notice");
    }

    [Fact]
    public async Task optimized_composite_rebuild_with_side_effect_publishing_completes()
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

        await using var store = (DocumentStore)DocumentStore.For(configure);
        foreach (var tenant in tenants)
        {
            await store.Advanced.AddTenantToShardAsync(tenant, shardAssignment[tenant], CancellationToken.None);
        }

        // Many streams per tenant so a rebuild page carries many parallel slices that all publish a
        // side-effect message -> concurrent CurrentMessageBatch calls (the #4727 deadlock window).
        const int streamsPerTenant = 25;
        foreach (var tenant in tenants)
        {
            await using var session = store.LightweightSession(tenant);
            for (var i = 0; i < streamsPerTenant; i++)
            {
                var id = Guid.NewGuid();
                session.Events.StartStream<CsTrip>(id, new TripStarted(id), new TripLeg(1.0), new TripLeg(2.5));
            }

            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync("tA");

        Exception? thrown = null;
        try
        {
            await daemon.RebuildProjectionAsync("CsComposite", 45.Seconds(), CancellationToken.None);
        }
        catch (Exception e)
        {
            thrown = e;
            _output.WriteLine(e.ToString());
        }

        thrown.ShouldBeNull(
            "the optimized composite rebuild with side-effect-publishing stage members must complete, " +
            "not deadlock on the ProjectionUpdateBatch message-batch semaphore (#4727)");

        // Documents for the 3 tenants on the multi-tenant shard materialized for both stages.
        foreach (var tenant in new[] { "tA", "tB", "tC" })
        {
            await using var session = store.QuerySession(tenant);
            (await session.Query<CsTrip>().CountAsync()).ShouldBe(streamsPerTenant);
            (await session.Query<CsTripNotice>().CountAsync()).ShouldBe(streamsPerTenant);
        }
    }

    private sealed class RecordingOutbox: IMessageOutbox
    {
        public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
            => new(new RecordingBatch());
    }

    private sealed class RecordingBatch: IMessageBatch
    {
        public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) => Task.CompletedTask;
        public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) => Task.CompletedTask;
        public ValueTask PublishAsync<T>(T message, string tenantId) => new();
    }
}
