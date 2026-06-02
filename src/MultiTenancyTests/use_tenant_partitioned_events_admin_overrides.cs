using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Progress;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4596 Phase 1 Session 4 — admin-API tenant overloads. jasperfx#407 Phase 0
/// shipped these as default interface methods that delegate to the existing
/// tenantless overload when <c>tenantId</c> is null and throw
/// <see cref="NotSupportedException"/> otherwise. Session 4 replaces those
/// throwing defaults with real Marten implementations:
/// <list type="bullet">
///   <item><description><see cref="IEventDatabase.FindEventStoreFloorAtTimeAsync(DateTimeOffset, string?, CancellationToken)"/> filters mt_events by tenant_id (the partitioning column from Phase 1 Session 1).</description></item>
///   <item><description><see cref="IEventDatabase.AllProjectionProgress(string?, CancellationToken)"/> filters mt_event_progression by trailing tenant suffix on the `name` column (the 3-segment ShardName.Identity from Session 3).</description></item>
///   <item><description><see cref="IEventStore.GetProjectionStatusesAsync(string?, CancellationToken)"/> composes per-tenant ShardNames for each registered projection and reports per-tenant progression.</description></item>
///   <item><description><see cref="IEventStore.DeleteProjectionProgressAsync(IEventDatabase, string, string?, CancellationToken)"/> scopes the DELETE to the tenant-bearing row identities.</description></item>
/// </list>
///
/// <para>
/// IProjectionDaemon's per-tenant <c>RebuildProjectionAsync</c> /
/// <c>RewindSubscriptionAsync</c> are intentionally NOT overridden — Marten's
/// daemon doesn't yet split per tenant (Phase 2 work), so the jasperfx
/// default's <see cref="NotSupportedException"/> is the correct surface for
/// now.
/// </para>
/// </summary>
public class use_tenant_partitioned_events_admin_overrides
{
    private const string Schema = "tenant_partitioned_events_session4";

    private static async Task ResetSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(schema); } catch (Exception) { }
    }

    private static DocumentStore BuildStore(string schema)
    {
        return DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();

            o.Events.AddEventType<TaggedEvent>();

            // Async projection so GetProjectionStatusesAsync / DeleteProjectionProgressAsync
            // have a real shard to iterate. Live aggregation wouldn't register an
            // async projection source.
            o.Projections.Add<TaggedAggregateProjection>(ProjectionLifecycle.Async);
        });
    }

    public record TaggedEvent(string Label);

    public class TaggedAggregate
    {
        public Guid Id { get; set; }
        public string? Label { get; set; }
        public void Apply(TaggedEvent e) => Label = e.Label;
    }

    public partial class TaggedAggregateProjection : SingleStreamProjection<TaggedAggregate, Guid>
    {
        public TaggedAggregateProjection()
        {
            Name = "TaggedAggregate";
        }
    }

    // ----- IEventDatabase.FindEventStoreFloorAtTimeAsync(timestamp, tenantId, token) -----

    [Fact]
    public async Task find_event_store_floor_at_time_scopes_to_one_tenant_when_tenantId_is_non_null()
    {
        var schema = Schema + "_floor";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await using (var sA = store.LightweightSession("alpha"))
        {
            sA.Events.StartStream(Guid.NewGuid(), new TaggedEvent("a"));
            await sA.SaveChangesAsync();
        }
        await using (var sB = store.LightweightSession("beta"))
        {
            sB.Events.StartStream(Guid.NewGuid(), new TaggedEvent("b"));
            await sB.SaveChangesAsync();
        }

        var db = (MartenDatabase)store.Storage.Database;
        var floorEpoch = DateTimeOffset.UtcNow.AddDays(-1);

        var alphaFloor = await db.FindEventStoreFloorAtTimeAsync(floorEpoch, tenantId: "alpha", CancellationToken.None);
        var betaFloor = await db.FindEventStoreFloorAtTimeAsync(floorEpoch, tenantId: "beta", CancellationToken.None);
        var globalFloor = await db.FindEventStoreFloorAtTimeAsync(floorEpoch, tenantId: null, CancellationToken.None);

        // Each tenant's per-tenant sequence starts at 1 (Session 2).
        alphaFloor.ShouldBe(1L);
        betaFloor.ShouldBe(1L);
        // Tenantless overload returns the earliest seq_id across all tenants;
        // both tenants' first event have seq_id=1, so the min is 1.
        globalFloor.ShouldBe(1L);
    }

    [Fact]
    public async Task find_event_store_floor_at_time_delegates_to_tenantless_when_tenant_is_null()
    {
        var schema = Schema + "_floor_null";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await using (var s = store.LightweightSession("alpha"))
        {
            s.Events.StartStream(Guid.NewGuid(), new TaggedEvent("hi"));
            await s.SaveChangesAsync();
        }

        var db = (MartenDatabase)store.Storage.Database;
        var epoch = DateTimeOffset.UtcNow.AddDays(-1);

        var nullTenant = await db.FindEventStoreFloorAtTimeAsync(epoch, tenantId: null, CancellationToken.None);
        var noTenant = await db.FindEventStoreFloorAtTimeAsync(epoch, CancellationToken.None);

        nullTenant.ShouldBe(noTenant);
    }

    // ----- IEventDatabase.AllProjectionProgress(tenantId, token) -----

    [Fact]
    public async Task all_projection_progress_filters_by_tenant_suffix_on_name_column()
    {
        var schema = Schema + "_progress";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Seed progression rows directly — write a per-tenant ShardName.Identity
        // for each tenant (the 3-segment grammar from jasperfx#407 Phase 0) so
        // the row names end in `:alpha` and `:beta` respectively.
        var alphaName = ShardName.Compose("OrdersProjection", tenantId: "alpha");
        var betaName = ShardName.Compose("OrdersProjection", tenantId: "beta");
        var globalName = ShardName.Compose("LegacyProjection");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(alphaName, 0, 17, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(betaName, 0, 42, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(globalName, 0, 99, agent: null!)));
            await session.SaveChangesAsync();
        }

        var db = (MartenDatabase)store.Storage.Database;

        var alphaOnly = await db.AllProjectionProgress(tenantId: "alpha", CancellationToken.None);
        var betaOnly = await db.AllProjectionProgress(tenantId: "beta", CancellationToken.None);
        var everything = await db.AllProjectionProgress(tenantId: null, CancellationToken.None);

        alphaOnly.Select(s => s.ShardName).ShouldHaveSingleItem().ShouldBe(alphaName.Identity);
        betaOnly.Select(s => s.ShardName).ShouldHaveSingleItem().ShouldBe(betaName.Identity);
        // Tenantless returns every row (today's behavior). Includes the high-water
        // row that the EnsureStorageExistsAsync bootstrap may have written, so
        // assert only that our 3 seeded rows are present.
        everything.Select(s => s.ShardName).ShouldContain(alphaName.Identity);
        everything.Select(s => s.ShardName).ShouldContain(betaName.Identity);
        everything.Select(s => s.ShardName).ShouldContain(globalName.Identity);
    }

    // ----- IEventStore.GetProjectionStatusesAsync(tenantId, ct) -----

    [Fact]
    public async Task get_projection_statuses_composes_tenant_bearing_shard_identities()
    {
        var schema = Schema + "_statuses";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Seed a progression row for the tenant-bearing identity of the
        // single registered projection (TaggedAggregate live-stream).
        var perTenantName = ShardName.Compose(nameof(TaggedAggregate), tenantId: "alpha");
        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(perTenantName, 0, 123, agent: null!)));
            await session.SaveChangesAsync();
        }

        var es = (IEventStore)store;
        var perTenantStatuses = await es.GetProjectionStatusesAsync(tenantId: "alpha", CancellationToken.None);

        var tagged = perTenantStatuses.SingleOrDefault(p => p.ProjectionName.Contains(nameof(TaggedAggregate), StringComparison.OrdinalIgnoreCase));
        tagged.ShouldNotBeNull();
        tagged.Shards.Count.ShouldBeGreaterThan(0);

        // The reported shard identity is the tenant-bearing form, and its
        // ProcessedSequence comes from the row we seeded.
        var perTenantShard = tagged.Shards.Single(s => s.ShardName.EndsWith(":alpha"));
        perTenantShard.ShardName.ShouldBe(perTenantName.Identity);
        perTenantShard.ProcessedSequence.ShouldBe(123L);
    }

    // ----- IEventStore.DeleteProjectionProgressAsync(database, name, tenantId, token) -----

    [Fact]
    public async Task delete_projection_progress_with_tenant_id_drops_only_that_tenants_rows()
    {
        var schema = Schema + "_delete";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var alphaName = ShardName.Compose(nameof(TaggedAggregate), tenantId: "alpha");
        var betaName = ShardName.Compose(nameof(TaggedAggregate), tenantId: "beta");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(alphaName, 0, 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(betaName, 0, 20, agent: null!)));
            await session.SaveChangesAsync();
        }

        var es = (IEventStore<IDocumentOperations, IQuerySession>)store;
        await es.DeleteProjectionProgressAsync((IEventDatabase)store.Storage.Database, nameof(TaggedAggregate), tenantId: "alpha",
            CancellationToken.None);

        var rows = await ReadProgressionRowsAsync(schema, nameof(TaggedAggregate));
        rows.Count.ShouldBe(1, "alpha's row was deleted; beta's row should remain.");
        rows[0].name.ShouldBe(betaName.Identity);
    }

    [Fact]
    public async Task delete_projection_progress_with_null_tenant_id_keeps_legacy_drop_all_behavior()
    {
        var schema = Schema + "_delete_legacy";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var alphaName = ShardName.Compose(nameof(TaggedAggregate), tenantId: "alpha");
        var betaName = ShardName.Compose(nameof(TaggedAggregate), tenantId: "beta");
        var globalName = ShardName.Compose(nameof(TaggedAggregate));

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(alphaName, 0, 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(betaName, 0, 20, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(globalName, 0, 5, agent: null!)));
            await session.SaveChangesAsync();
        }

        var es = (IEventStore<IDocumentOperations, IQuerySession>)store;
        // Null tenant → today's behavior: drop every registered shard's
        // tenantless name for this projection. The per-tenant rows are
        // left in place because their names are tenant-bearing identities,
        // not in the registered shard list.
        await es.DeleteProjectionProgressAsync((IEventDatabase)store.Storage.Database, nameof(TaggedAggregate), tenantId: null,
            CancellationToken.None);

        var rows = await ReadProgressionRowsAsync(schema, nameof(TaggedAggregate));
        rows.Select(r => r.name).ShouldNotContain(globalName.Identity, "tenantless row gets dropped");
        rows.Select(r => r.name).ShouldContain(alphaName.Identity, "per-tenant rows survive the legacy delete");
        rows.Select(r => r.name).ShouldContain(betaName.Identity);
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<(string name, long last_seq_id)>>
        ReadProgressionRowsAsync(string schema, string namePrefix)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select name, last_seq_id from {schema}.mt_event_progression where name like @n order by name";
        cmd.Parameters.AddWithValue("n", namePrefix + "%");
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new System.Collections.Generic.List<(string, long)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }
        return rows;
    }
}
