using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4596 Phase 2c — end-to-end per-tenant rebuild isolation. Pins the
/// Marten-side contract that <c>RebuildProjectionAsync(name, tenantId, …)</c>
/// touches ONLY that one tenant's data:
///
/// <list type="bullet">
///   <item><description><b>Events:</b> the loader's SQL adds a literal
///   <c>d.tenant_id = '$tenant'</c> predicate so Postgres partition-prunes
///   <c>mt_events</c> (verified by inspecting
///   <see cref="EventLoader.TenantFilterValue"/>), AND the per-tenant
///   execution never sees another tenant's events — which would otherwise
///   route document writes via <see cref="IEvent.TenantId"/> to the wrong
///   tenant.</description></item>
///
///   <item><description><b>Progression:</b> only the
///   <c>{name}:{shardKey}:{tenantId}</c> row is reset+repopulated; the
///   sibling tenant's <c>(name, tenant_id)</c> row is untouched (same
///   <c>last_seq_id</c> before and after).</description></item>
///
///   <item><description><b>Documents:</b> only the rebuilding tenant's
///   projected docs are wiped+rewritten; the sibling tenant's docs survive
///   intact (the existing-docs query returns the same set before and after
///   the rebuild).</description></item>
/// </list>
///
/// <para>
/// JasperFx 2.5.0-pt209.5 (jasperfx#407 Phase 2c) threads <see cref="ShardName"/>
/// into <c>IEventStore.BuildEventLoader</c> so Marten can do the per-tenant SQL
/// scoping. Phase 2b (pt209.4) already added per-tenant
/// <c>RebuildProjectionAsync(name, tenantId, …)</c> +
/// <c>DeleteProjectionProgressAsync(name, tenantId, …)</c> on the daemon side;
/// this test exercises the full Marten consumption end-to-end.
/// </para>
/// </summary>
// Outer is partial because the nested TripDistanceProjection is partial (the
// source generator needs the partial chain to add the dispatcher method).
public partial class use_tenant_partitioned_events_per_tenant_rebuild
{
    // Schema name includes the running process id so the net9.0 and net10.0
    // test runs (which `dotnet test` launches in parallel against the same
    // database by default) get distinct schemas. Without this, the per-tenant
    // partitions created by AddMartenManagedTenantsAsync from one TFM's process
    // collide with the other's ResetSchemaAsync + EnsureStorageExistsAsync
    // sequence, surfacing as "42P07 relation mt_streams_alpha already exists".
    private static readonly string Schema =
        $"tenant_partitioned_events_session_p2c_p{Environment.ProcessId}";

    private static async Task ResetSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        // DROP SCHEMA … CASCADE wipes the parent partitioned tables (mt_streams,
        // mt_events) and the per-tenant partition children that were attached
        // to them, so a re-run of the test starts from a clean slate.
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

            o.Events.AddEventType<TripStarted>();
            o.Events.AddEventType<TripLeg>();

            // Nested-class type names blow past Postgres' 64-byte identifier limit
            // (mt_doc_<assembly-mangled-path>_tripdistance) — give the doc a short
            // alias so the auto-generated mt_doc_<alias> table fits.
            o.Schema.For<TripDistance>().DocumentAlias("p2c_trip_dist");

            o.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
        });
    }

    public record TripStarted(string Label);
    public record TripLeg(int Miles);

    public class TripDistance
    {
        public Guid Id { get; set; }
        public string? Label { get; set; }
        public int TotalMiles { get; set; }
        public int LegCount { get; set; }
    }

    // Subclass-with-convention-methods → must be partial so the JasperFx source
    // generator can emit the dispatcher. Self-aggregating types (Snapshot<T>) do
    // not need this, but subclassed SingleStreamProjection does.
    public partial class TripDistanceProjection : SingleStreamProjection<TripDistance, Guid>
    {
        public TripDistanceProjection()
        {
            Name = "TripDistance";
        }

        public void Apply(TripStarted e, TripDistance state) => state.Label = e.Label;

        public void Apply(TripLeg e, TripDistance state)
        {
            state.TotalMiles += e.Miles;
            state.LegCount++;
        }
    }

    // ---- The loader-level switch fires only when both flags align ----

    [Fact]
    public void event_loader_applies_tenant_filter_only_when_shard_carries_tenant_AND_partitioned_events_is_on()
    {
        // Off path: tenantless shard → no filter even with the flag on.
        using var store = BuildStore(Schema + "_loader_off");
        var db = (MartenDatabase)store.Storage.Database;
        var globalShard = ShardName.Compose("TripDistance");

        var loaderGlobal = new EventLoader(
            store, db, new AsyncOptions(), Array.Empty<Weasel.Postgresql.SqlGeneration.ISqlFragment>(), globalShard);
        loaderGlobal.TenantFilterValue.ShouldBeNull(
            "tenantless shard → loader stays partition-agnostic even with UseTenantPartitionedEvents on");

        // On path: tenant-bearing shard + UseTenantPartitionedEvents on → loader scopes.
        var tenantShard = globalShard.ForTenant("alpha");
        var loaderTenant = new EventLoader(
            store, db, new AsyncOptions(), Array.Empty<Weasel.Postgresql.SqlGeneration.ISqlFragment>(), tenantShard);
        loaderTenant.TenantFilterValue.ShouldBe("alpha",
            "per-tenant rebuild shard must surface its tenant slot to the loader so the SQL can partition-prune mt_events");
    }

    [Fact]
    public void event_loader_does_not_apply_tenant_filter_when_partitioned_events_is_off()
    {
        var schema = Schema + "_loader_unpartitioned";
        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            // UseTenantPartitionedEvents intentionally OFF — even though the
            // shard carries a tenant slot, the loader must not invent the filter
            // because mt_events isn't partitioned and the literal would gain
            // nothing (and could miss the index).
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Events.AddEventType<TripStarted>();
        });

        var db = (MartenDatabase)store.Storage.Database;
        var tenantShard = ShardName.Compose("TripDistance").ForTenant("alpha");

        var loader = new EventLoader(
            store, db, new AsyncOptions(), Array.Empty<Weasel.Postgresql.SqlGeneration.ISqlFragment>(), tenantShard);
        loader.TenantFilterValue.ShouldBeNull(
            "UseTenantPartitionedEvents off → loader stays partition-agnostic even on a tenant shard");
    }

    // ---- DeleteProjectionProgressAsync(tenantId) wipes ONLY the right tenant ----

    [Fact]
    public async Task delete_projection_progress_with_tenant_id_wipes_only_that_tenants_docs_and_progression()
    {
        var schema = Schema + "_isolation";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Seed two tenants with distinct stream + projected-doc state.
        var alphaStreamId = Guid.NewGuid();
        var betaStreamId = Guid.NewGuid();

        await using (var session = store.LightweightSession("alpha"))
        {
            session.Events.StartStream<TripDistance>(alphaStreamId,
                new TripStarted("alpha-trip"), new TripLeg(10), new TripLeg(20));
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("beta"))
        {
            session.Events.StartStream<TripDistance>(betaStreamId,
                new TripStarted("beta-trip"), new TripLeg(7), new TripLeg(13));
            await session.SaveChangesAsync();
        }

        // Drive the projection forward on both tenants so docs materialise.
        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(10.Seconds());
            await daemon.StopAllAsync();
        }

        // Pre-rebuild snapshot of both tenants' data + the store-global
        // progression. Catch-up runs as a CONTINUOUS daemon, so it writes the
        // store-global "TripDistance:All" progression row, NOT per-tenant rows
        // (per-tenant naming only kicks in on the rebuild path that rebinds the
        // shard via ShardName.ForTenant).
        TripDistance? alphaBefore;
        TripDistance? betaBefore;
        await using (var session = store.QuerySession("alpha"))
        {
            alphaBefore = await session.LoadAsync<TripDistance>(alphaStreamId);
        }
        await using (var session = store.QuerySession("beta"))
        {
            betaBefore = await session.LoadAsync<TripDistance>(betaStreamId);
        }
        alphaBefore.ShouldNotBeNull("alpha's projection should have materialised before the per-tenant reset");
        alphaBefore.TotalMiles.ShouldBe(30, "alpha starts at 10 + 20 = 30 miles");
        betaBefore.ShouldNotBeNull("beta's projection should have materialised before the per-tenant reset");
        betaBefore.TotalMiles.ShouldBe(20, "beta starts at 7 + 13 = 20 miles");

        var globalProgressionName = ShardName.Compose("TripDistance").Identity;
        var globalProgressionBefore = await ReadProgressionAsync(schema, globalProgressionName);
        globalProgressionBefore.ShouldBeGreaterThan(0L,
            "continuous catch-up should have written the store-global progression row");

        // The action: tenant-scoped pre-rebuild reset for alpha. This is the
        // exact hook jasperfx#407 Phase 2b's rebuildProjectionForTenant calls
        // right before kicking off a per-tenant replay — Marten bundles the
        // tenant-scoped doc teardown into it (#4596 Phase 2c) so the rebuild
        // wipes only the rebuilding tenant's docs.
        var es = (IEventStore<IDocumentOperations, IQuerySession>)store;
        await es.DeleteProjectionProgressAsync(
            (IEventDatabase)store.Storage.Database, "TripDistance", tenantId: "alpha", CancellationToken.None);

        // Alpha's doc should be GONE; beta's must be byte-identical.
        await using (var session = store.QuerySession("alpha"))
        {
            (await session.LoadAsync<TripDistance>(alphaStreamId))
                .ShouldBeNull("alpha's projected doc must be wiped by the tenant-scoped teardown");
        }
        await using (var session = store.QuerySession("beta"))
        {
            var betaAfter = await session.LoadAsync<TripDistance>(betaStreamId);
            betaAfter.ShouldNotBeNull("beta's doc must survive — its tenant slot was never named");
            betaAfter!.TotalMiles.ShouldBe(betaBefore.TotalMiles,
                "beta's projection state must be byte-identical pre/post the per-tenant teardown");
            betaAfter.LegCount.ShouldBe(betaBefore.LegCount);
            betaAfter.Label.ShouldBe(betaBefore.Label);
        }

        // The store-global progression row must be untouched — the per-tenant
        // overload of DeleteProjectionProgressAsync only targets the
        // tenant-bearing "{name}:{shardKey}:{tenantId}" identities (Phase 1
        // Session 4), and the continuous catch-up's "{name}:{shardKey}" row
        // belongs to no tenant.
        (await ReadProgressionAsync(schema, globalProgressionName))
            .ShouldBe(globalProgressionBefore,
                "store-global progression row must survive a per-tenant reset — it doesn't belong to any tenant");
    }

    private static async Task<long> ReadProgressionAsync(string schema, string name)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select last_seq_id from {schema}.mt_event_progression where name = @n";
        cmd.Parameters.AddWithValue("n", name);
        var v = await cmd.ExecuteScalarAsync();
        return v == null || v == DBNull.Value ? 0L : Convert.ToInt64(v);
    }
}
