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
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// #4797 — under <c>MultiTenantedWithShardedDatabases</c> + <c>UseTenantPartitionedEvents</c>, the
/// projection progress-reading APIs (<c>IDocumentStore.Advanced.AllProjectionProgress</c> /
/// <c>ProjectionProgressFor</c>) used to fall back to <c>Tenancy.Default.Database</c> when the
/// tenant id was omitted, and <c>ShardedTenancy.Default</c> throws <c>NotSupportedException</c> —
/// so the calls threw every time even though their XML docs promised a default-database fallback.
///
/// <para>
/// The fix mirrors <c>WaitForNonStaleProjectionDataAsync</c>'s sharded-aware shape (#4366): a null
/// tenant id now fans out across every database the store knows about. <c>AllProjectionProgress</c>
/// concatenates each shard's progression rows (per-tenant rows stay attributable via the
/// <c>{Name}:{ShardKey}:{tenantId}</c> identity grammar); <c>ProjectionProgressFor</c> returns the
/// highest progression found for the shard name — for a tenant-qualified identity that row only
/// exists in the single shard owning the tenant, so the result is that tenant's exact progression.
/// </para>
///
/// <para>
/// Own-store: needs explicit per-shard tenant assignment via <c>AddTenantToShardAsync</c>. Uses its
/// own schema names + daemon lock id so sibling agents sharing the physical shard databases don't
/// collide with this test's objects.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class sharded_progress_reading_4797: IAsyncLifetime
{
    private const string MasterSchema = "sharded4797";
    private const string PartitionSchema = "tenants4797";
    private const string EventsSchema = "prog4797";
    private const int LockId = 47971;

    private readonly ShardedPartitionedFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_progress_reading_4797(ShardedPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(MasterSchema); } catch { }

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var shardConn = new NpgsqlConnection(connStr);
            await shardConn.OpenAsync();
            try { await shardConn.DropSchemaAsync(PartitionSchema); } catch { }
            try { await shardConn.DropSchemaAsync(EventsSchema); } catch { }
        }
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task progress_reading_with_null_tenant_spans_all_shard_databases()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = MasterSchema;
                x.PartitionSchemaName = PartitionSchema;

                foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
            });

            opts.DatabaseSchemaName = EventsSchema;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<Progress4797Event>();

            opts.Projections.Add<Progress4797Projection>(ProjectionLifecycle.Async);
            opts.Projections.DaemonLockId = LockId;
            opts.Schema.For<Progress4797Doc>().DocumentAlias("p4797_doc");
        });

        // This test uses its own DatabaseSchemaName (sibling isolation), so provision
        // the schema in every shard database up front — AddTenantToShardAsync's
        // per-tenant sequence DDL targets the events schema directly and does not
        // lazily create it the way a first session would.
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Two tenants on two DIFFERENT shards so the progress rows genuinely
        // live in separate databases and only a cross-shard read can see both.
        var shardA = _fixture.DbNames[0];
        var shardB = _fixture.DbNames[1];
        await _store.Advanced.AddTenantToShardAsync("t4797_a", shardA, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("t4797_b", shardB, CancellationToken.None);

        var aStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("t4797_a"))
        {
            session.Events.StartStream<Progress4797Doc>(aStream,
                new Progress4797Event("a-1"), new Progress4797Event("a-2"), new Progress4797Event("a-3"));
            await session.SaveChangesAsync();
        }

        var bStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("t4797_b"))
        {
            session.Events.StartStream<Progress4797Doc>(bStream,
                new Progress4797Event("b-1"), new Progress4797Event("b-2"));
            await session.SaveChangesAsync();
        }

        // One daemon per shard (BuildProjectionDaemonAsync needs an explicit
        // tenant/database under sharded tenancy).
        using var daemonA = await _store.BuildProjectionDaemonAsync("t4797_a");
        using var daemonB = await _store.BuildProjectionDaemonAsync("t4797_b");
        await daemonA.StartAllAsync();
        await daemonB.StartAllAsync();
        await daemonA.WaitForNonStaleData(20.Seconds());
        await daemonB.WaitForNonStaleData(20.Seconds());

        // THE #4797 PIN: AllProjectionProgress() with an omitted tenant used to throw
        // NotSupportedException out of ShardedTenancy.get_Default(). It must now fan out
        // across the shard databases. Poll for the per-tenant rows — WaitForNonStaleData
        // can return before a per-tenant agent commits its progression row on a
        // partitioned store.
        long SeqFor(IReadOnlyList<ShardState> states, string tenantId)
            => states.SingleOrDefault(x =>
                    x.ShardName.StartsWith(Progress4797Projection.ProjectionName + ":", StringComparison.Ordinal)
                    && x.ShardName.EndsWith(":" + tenantId, StringComparison.Ordinal))
                ?.Sequence ?? 0;

        IReadOnlyList<ShardState> all = [];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < 30.Seconds())
        {
            all = await _store.Advanced.AllProjectionProgress();
            if (SeqFor(all, "t4797_a") >= 3 && SeqFor(all, "t4797_b") >= 2)
            {
                break;
            }

            await Task.Delay(250);
        }

        // Per-tenant projection rows from BOTH shard databases show up in one read,
        // each at its own tenant's height (3 events for a, 2 for b — no bleed-over).
        SeqFor(all, "t4797_a").ShouldBe(3,
            "tenant a's projection progression row (shard A) must be included and at its own height");
        SeqFor(all, "t4797_b").ShouldBe(2,
            "tenant b's projection progression row (shard B) must be included and at its own height");

        // The tenant-scoped overload keeps its documented "database containing this
        // tenant id" semantics: it reads ONLY that tenant's shard database.
        var onlyShardA = await _store.Advanced.AllProjectionProgress("t4797_a");
        SeqFor(onlyShardA, "t4797_a").ShouldBe(3);
        SeqFor(onlyShardA, "t4797_b").ShouldBe(0,
            "tenant b lives on a different shard database, so its rows must not appear");

        // ProjectionProgressFor with an omitted tenant also used to throw. For a
        // tenant-qualified shard identity the row exists only in the owning shard,
        // so the cross-shard read returns that tenant's exact progression.
        var tenantAShard = ShardName.Compose(
            Progress4797Projection.ProjectionName, "All", "t4797_a", 1);
        (await _store.Advanced.ProjectionProgressFor(tenantAShard)).ShouldBe(3);

        var tenantBShard = ShardName.Compose(
            Progress4797Projection.ProjectionName, "All", "t4797_b", 1);
        (await _store.Advanced.ProjectionProgressFor(tenantBShard)).ShouldBe(2);

        // And the issue's literal repro: the store-global HighWaterMark identity exists
        // per shard database; the null-tenant read returns the highest one instead of
        // throwing. Shard A saw 3 events on its per-tenant sequence, so the max is >= 3.
        var highWater = await _store.Advanced.ProjectionProgressFor(new ShardName(ShardState.HighWaterMark));
        highWater.ShouldBeGreaterThanOrEqualTo(3);

        // A shard identity that exists nowhere reports 0, not an exception.
        (await _store.Advanced.ProjectionProgressFor(new ShardName("NoSuchProjection"))).ShouldBe(0);
    }
}

public record Progress4797Event(string Label);

public class Progress4797Doc
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class Progress4797Projection: SingleStreamProjection<Progress4797Doc, Guid>
{
    public const string ProjectionName = "Progress4797";

    public Progress4797Projection()
    {
        Name = ProjectionName;
    }

    public void Apply(Progress4797Doc doc, Progress4797Event _) => doc.Count++;
}
