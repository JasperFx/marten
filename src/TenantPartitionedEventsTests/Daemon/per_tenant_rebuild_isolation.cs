using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_per_tenant_rebuild.cs
/// — #4596 Phase 2c end-to-end per-tenant rebuild isolation. Pins that
/// <c>DeleteProjectionProgressAsync(name, tenantId, …)</c> touches ONLY that
/// one tenant's projected docs + progression row, leaving every sibling tenant
/// byte-identical.
///
/// <para>
/// Runs against the shared <see cref="GuidPartitionedFixture"/> and mints
/// unique tenant ids per test via <see cref="PartitionedFixtureBase.NewTenant"/>
/// so the assertions never collide with sibling tests' progression rows on the
/// shared store. The fixture pre-registers
/// <see cref="TripDistanceProjection"/> as Async with the deterministic name
/// <see cref="TripDistanceProjection.ProjectionName"/> — the per-tenant
/// <c>{name}:{shardKey}:{tenant}</c> identity composes off that.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class per_tenant_rebuild_isolation
{
    private readonly GuidPartitionedFixture _fixture;

    public per_tenant_rebuild_isolation(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task delete_projection_progress_with_tenant_id_wipes_only_that_tenants_docs_and_progression()
    {
        // Unique tenant ids per test so the sibling-survival assertions never
        // race against another test's tenant-bearing progression row on the
        // shared store.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Seed two tenants with distinct stream + projected-doc state. The
        // fixture's TripDistance has `Distance` (not `TotalMiles`) and its
        // projection sums TripLeg.Distance per stream.
        var alphaStreamId = Guid.NewGuid();
        var betaStreamId = Guid.NewGuid();

        await using (var session = _fixture.Store.LightweightSession(alpha))
        {
            session.Events.StartStream<TripDistance>(alphaStreamId,
                new TripStarted(alphaStreamId), new TripLeg(10), new TripLeg(20));
            await session.SaveChangesAsync();
        }

        await using (var session = _fixture.Store.LightweightSession(beta))
        {
            session.Events.StartStream<TripDistance>(betaStreamId,
                new TripStarted(betaStreamId), new TripLeg(7), new TripLeg(13));
            await session.SaveChangesAsync();
        }

        // Drive the projection forward on both tenants. Use per-tenant
        // RebuildProjectionAsync (the jasperfx#407 entry point) rather than the
        // continuous StartAllAsync catch-up because the catch-up writes a
        // SHARED store-global progression row ("TripDistance:All") — under the
        // shared fixture, that row may already be advanced past this test's
        // tenants' events from prior tests, which would make the catch-up a
        // no-op and the projection docs would never materialise. The per-tenant
        // rebuild explicitly walks the tenant's events from scratch.
        using (var daemon = await _fixture.Store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(TripDistanceProjection.ProjectionName, alpha, CancellationToken.None);
            await daemon.RebuildProjectionAsync(TripDistanceProjection.ProjectionName, beta, CancellationToken.None);
        }

        // Pre-rebuild snapshot of both tenants' data + the store-global
        // progression row written by the continuous catch-up. Catch-up runs
        // as CONTINUOUS, so it writes the store-global "TripDistance:All" row
        // — per-tenant naming only kicks in on the rebuild path that rebinds
        // the shard via ShardName.ForTenant.
        TripDistance alphaBefore;
        TripDistance betaBefore;
        await using (var session = _fixture.Store.QuerySession(alpha))
        {
            alphaBefore = await session.LoadAsync<TripDistance>(alphaStreamId);
        }
        await using (var session = _fixture.Store.QuerySession(beta))
        {
            betaBefore = await session.LoadAsync<TripDistance>(betaStreamId);
        }
        alphaBefore.ShouldNotBeNull("alpha's projection should have materialised before the per-tenant reset");
        alphaBefore.Distance.ShouldBe(30, "alpha starts at 10 + 20 = 30 miles");
        betaBefore.ShouldNotBeNull("beta's projection should have materialised before the per-tenant reset");
        betaBefore.Distance.ShouldBe(20, "beta starts at 7 + 13 = 20 miles");

        // Snapshot the store-global "TripDistance:All" progression row. Sibling
        // tests on the shared fixture may have written it (via continuous
        // catch-up) or not — either way, the final assertion below pins that
        // the per-tenant delete must leave THIS value unchanged.
        var globalProgressionName = ShardName.Compose(TripDistanceProjection.ProjectionName).Identity;
        var globalProgressionBefore = await ReadProgressionAsync(_fixture.SchemaName, globalProgressionName);

        // The action: tenant-scoped pre-rebuild reset for alpha. This is the
        // exact hook jasperfx#407 Phase 2b's rebuildProjectionForTenant calls
        // right before kicking off a per-tenant replay — Marten bundles the
        // tenant-scoped doc teardown into it (#4596 Phase 2c) so the rebuild
        // wipes only the rebuilding tenant's docs.
        var es = (IEventStore<IDocumentOperations, IQuerySession>)_fixture.Store;
        await es.DeleteProjectionProgressAsync(
            (IEventDatabase)_fixture.Store.Storage.Database,
            TripDistanceProjection.ProjectionName, tenantId: alpha, CancellationToken.None);

        // Alpha's doc should be GONE; beta's must be byte-identical.
        await using (var session = _fixture.Store.QuerySession(alpha))
        {
            (await session.LoadAsync<TripDistance>(alphaStreamId))
                .ShouldBeNull("alpha's projected doc must be wiped by the tenant-scoped teardown");
        }
        await using (var session = _fixture.Store.QuerySession(beta))
        {
            var betaAfter = await session.LoadAsync<TripDistance>(betaStreamId);
            betaAfter.ShouldNotBeNull("beta's doc must survive — its tenant slot was never named");
            betaAfter!.Distance.ShouldBe(betaBefore.Distance,
                "beta's projection state must be byte-identical pre/post the per-tenant teardown");
            betaAfter.Version.ShouldBe(betaBefore.Version);
        }

        // The store-global progression row must be untouched — the per-tenant
        // overload of DeleteProjectionProgressAsync only targets the
        // tenant-bearing "{name}:{shardKey}:{tenantId}" identities (Phase 1
        // Session 4), and the continuous catch-up's "{name}:{shardKey}" row
        // belongs to no tenant.
        (await ReadProgressionAsync(_fixture.SchemaName, globalProgressionName))
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
