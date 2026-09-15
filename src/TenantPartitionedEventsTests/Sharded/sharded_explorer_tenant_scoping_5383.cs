#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// marten#5383 gap 2 — the explorer reads decide whether to filter on <c>tenant_id</c> by
/// <see cref="DatabaseCardinality"/>, and that is the wrong axis for
/// <see cref="Marten.Storage.ShardedTenancy"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>DocumentStore.EventStoreExplorer.cs</c> computes
/// <c>scopeByColumn = tenantId != null &amp;&amp; Cardinality == Single</c>, and its own doc comment
/// states the premise: <i>"Database-per-tenant / sharded: the argument names a physical database, so
/// the session is opened against that tenant's own database and no tenant_id filter is needed — every
/// stream in that database already belongs to the tenant."</i>
/// </para>
/// <para>
/// That premise holds for database-per-tenant and is <b>false for sharded tenancy</b>, which
/// distributes tenants across a POOL of databases with conjoined event tenancy: many tenants share
/// each database. Opening the tenant's database narrows the answer to that shard and no further, so
/// one tenant's explorer read returns every co-located tenant's data.
/// </para>
/// <para>
/// ⚠️ <b>The two axes are independent.</b> "Which database do I open" is a question about
/// cardinality; "do I filter rows" is a question about whether events are conjoined. Sharded tenancy
/// is the configuration that needs BOTH, and it is the one the single boolean cannot express.
/// </para>
/// <para>
/// The issue filed this as a code-read and said so — <i>"No repro was built — treat this as a
/// pointer, not a diagnosis."</i> This is the repro, written before any fix, and it pins the leak in
/// the direction it turned out to have.
/// </para>
/// </remarks>
[Collection("sharded-tenant-partitioned")]
public class sharded_explorer_tenant_scoping_5383 : ShardedPartitionedContext
{
    public sharded_explorer_tenant_scoping_5383(ShardedPartitionedFixture fixture) : base(fixture)
    {
    }

    /// <summary>Two tenants deliberately co-located on ONE shard — the configuration under test.</summary>
    private async Task<IDocumentStore> coLocatedStoreAsync(params string[] tenants)
    {
        var store = TrackStore(DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";
                foreach (var (dbName, connStr) in Fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
            });
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            // String keys so a stream can carry the SAME id under two tenants — the interleaving case.
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedTestEvent>();
        }));

        // Explicit assignment, not the hash distribution: the whole point is that these two share a
        // database, and a test that depended on where an auto-assigner put them would be testing the
        // assigner.
        var sharedShard = Fixture.DbNames[0];
        foreach (var tenant in tenants)
        {
            await store.Advanced.AddTenantToShardAsync(tenant, sharedShard, CancellationToken.None);
        }

        return store;
    }

    private static async Task<string> appendAsync(IDocumentStore store, string tenantId, string marker, string? streamKey = null)
    {
        var key = streamKey ?? $"{tenantId}-{marker}";
        await using var session = store.LightweightSession(tenantId);
        session.Events.Append(key, new ShardedTestEvent { Value = marker });
        await session.SaveChangesAsync();
        return key;
    }

    [Fact]
    public async Task recent_streams_for_one_tenant_must_not_list_a_co_located_tenants_streams()
    {
        var store = await coLocatedStoreAsync("alpha", "beta");
        await appendAsync(store, "alpha", "alpha-one");
        await appendAsync(store, "beta", "beta-one");

        var streams = await ((IEventStore)store).GetRecentStreamsAsync(50, "alpha", CancellationToken.None);
        var keys = streams.Select(x => x.StreamId).ToList();

        keys.ShouldContain("alpha-alpha-one");
        keys.ShouldNotContain("beta-beta-one",
            "alpha and beta share a shard under conjoined tenancy, so opening the tenant's DATABASE "
            + "does not scope the answer — the tenant_id predicate is what does");
    }

    [Fact]
    public async Task reading_a_stream_for_one_tenant_must_not_return_a_co_located_tenants_events()
    {
        var store = await coLocatedStoreAsync("alpha", "beta");

        // The SAME stream key under both tenants — legal under conjoined tenancy, where the stream's
        // identity is (tenant_id, id). This is the case that interleaves two tenants' events into one
        // version-ordered sequence, which is worse than a leak: it is a wrong document.
        const string shared = "order-1";
        await appendAsync(store, "alpha", "from-alpha", shared);
        await appendAsync(store, "beta", "from-beta", shared);

        var events = new List<EventRecord>();
        await foreach (var e in ((IEventStore)store).ReadStreamAsync(shared, "alpha", CancellationToken.None))
        {
            events.Add(e);
        }

        events.ShouldNotBeEmpty();
        events.Select(x => x.TenantId).Distinct().ShouldBe(["alpha"],
            "a tenant-scoped stream read on a sharded store must return only that tenant's events");
    }
}
