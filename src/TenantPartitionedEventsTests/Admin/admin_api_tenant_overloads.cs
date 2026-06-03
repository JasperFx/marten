using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Progress;
using Marten.Storage;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_admin_overrides.cs
/// — same Phase 1 Session 4 admin-API tenant overloads, now exercised against
/// the shared <see cref="GuidPartitionedFixture"/>. Per-test isolation lives on
/// unique tenant ids minted from <see cref="PartitionedFixtureBase.NewTenant"/>
/// and on unique projection names so two tests' progression rows never collide
/// in the shared store's <c>mt_event_progression</c> table.
/// </summary>
[Collection("guid-partitioned")]
public class admin_api_tenant_overloads
{
    private readonly GuidPartitionedFixture _fixture;

    public admin_api_tenant_overloads(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    private static string NewProjectionName() =>
        "p_" + Guid.NewGuid().ToString("N")[..10];

    // ----- IEventDatabase.FindEventStoreFloorAtTimeAsync(timestamp, tenantId, token) -----

    [Fact]
    public async Task find_event_store_floor_at_time_scopes_to_one_tenant_when_tenantId_is_non_null()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Each tenant's first event lands at seq_id = 1 (per-tenant sequence
        // starts fresh — the headline guarantee of partitioning).
        await _fixture.AppendNEventsAsync(alpha, 1);
        await _fixture.AppendNEventsAsync(beta, 1);

        var db = (MartenDatabase)_fixture.Store.Storage.Database;
        var floorEpoch = DateTimeOffset.UtcNow.AddDays(-1);

        var alphaFloor = await db.FindEventStoreFloorAtTimeAsync(floorEpoch, tenantId: alpha, CancellationToken.None);
        var betaFloor = await db.FindEventStoreFloorAtTimeAsync(floorEpoch, tenantId: beta, CancellationToken.None);

        // Each tenant's per-tenant sequence starts at 1 (Session 2) — independent
        // of what's been written for other tenants in this shared store.
        alphaFloor.ShouldBe(1L);
        betaFloor.ShouldBe(1L);
    }

    [Fact]
    public async Task find_event_store_floor_at_time_delegates_to_tenantless_when_tenant_is_null()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);
        await _fixture.AppendNEventsAsync(tenant, 1);

        var db = (MartenDatabase)_fixture.Store.Storage.Database;
        var epoch = DateTimeOffset.UtcNow.AddDays(-1);

        var nullTenant = await db.FindEventStoreFloorAtTimeAsync(epoch, tenantId: null, CancellationToken.None);
        var noTenant = await db.FindEventStoreFloorAtTimeAsync(epoch, CancellationToken.None);

        // Both null-tenant calls hit the same code path; identical result regardless
        // of what the shared store's running min(seq_id) happens to be.
        nullTenant.ShouldBe(noTenant);
    }

    // ----- IEventDatabase.AllProjectionProgress(tenantId, token) -----

    [Fact]
    public async Task all_projection_progress_filters_by_tenant_suffix_on_name_column()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Unique projection names so siblings tests' seeded rows don't bleed into
        // our assertions. The 3 row names below are this test's "controlled set".
        var projection = NewProjectionName();
        var alphaName = ShardName.Compose(projection, tenantId: alpha);
        var betaName = ShardName.Compose(projection, tenantId: beta);
        var globalName = ShardName.Compose(projection + "_legacy");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(alphaName, 0, 17, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(betaName, 0, 42, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(globalName, 0, 99, agent: null!)));
            await session.SaveChangesAsync();
        }

        var db = (MartenDatabase)_fixture.Store.Storage.Database;

        var alphaOnly = await db.AllProjectionProgress(tenantId: alpha, CancellationToken.None);
        var betaOnly = await db.AllProjectionProgress(tenantId: beta, CancellationToken.None);
        var everything = await db.AllProjectionProgress(tenantId: null, CancellationToken.None);

        // tenant filter is a SQL LIKE on a trailing `:tenant` suffix, so this test's
        // unique tenant id is the only matching row even on a shared store.
        alphaOnly.Select(s => s.ShardName).ShouldHaveSingleItem().ShouldBe(alphaName.Identity);
        betaOnly.Select(s => s.ShardName).ShouldHaveSingleItem().ShouldBe(betaName.Identity);
        // The tenantless query returns EVERY row in the table — including other
        // tests' progression rows on the shared store. Assert only that OUR 3
        // seeded rows are present, not that the result is exactly 3 rows.
        everything.Select(s => s.ShardName).ShouldContain(alphaName.Identity);
        everything.Select(s => s.ShardName).ShouldContain(betaName.Identity);
        everything.Select(s => s.ShardName).ShouldContain(globalName.Identity);
    }

    // ----- IEventStore.GetProjectionStatusesAsync(tenantId, ct) -----

    [Fact]
    public async Task get_projection_statuses_composes_tenant_bearing_shard_identities()
    {
        // GetProjectionStatusesAsync iterates the registered projections, so the
        // assertion target has to be a projection THIS store knows about. The
        // fixture pre-registers TripDistanceProjection with an explicit
        // .Name = "TripDistance" — use it.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var perTenantName = ShardName.Compose(TripDistanceProjection.ProjectionName, tenantId: tenant);
        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(tenant))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(perTenantName, 0, 123, agent: null!)));
            await session.SaveChangesAsync();
        }

        var es = (IEventStore)_fixture.Store;
        var perTenantStatuses = await es.GetProjectionStatusesAsync(tenantId: tenant, CancellationToken.None);

        var tripDistance = perTenantStatuses.SingleOrDefault(
            p => p.ProjectionName.Contains(TripDistanceProjection.ProjectionName, StringComparison.OrdinalIgnoreCase));
        tripDistance.ShouldNotBeNull();
        tripDistance.Shards.Count.ShouldBeGreaterThan(0);

        var perTenantShard = tripDistance.Shards.Single(s => s.ShardName.EndsWith(":" + tenant));
        perTenantShard.ShardName.ShouldBe(perTenantName.Identity);
        perTenantShard.ProcessedSequence.ShouldBe(123L);
    }

    // ----- IEventStore.DeleteProjectionProgressAsync(database, name, tenantId, token) -----

    [Fact]
    public async Task delete_projection_progress_with_tenant_id_drops_only_that_tenants_rows()
    {
        // DeleteProjectionProgressAsync requires the projection name to be REGISTERED,
        // so we use the fixture's TripDistance name. Tenant isolation comes from
        // unique tenant ids — the row names end in `:<tenant>` and never collide
        // with sibling tests' seeded rows.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var projection = TripDistanceProjection.ProjectionName;
        var alphaName = ShardName.Compose(projection, tenantId: alpha);
        var betaName = ShardName.Compose(projection, tenantId: beta);

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(alphaName, 0, 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(betaName, 0, 20, agent: null!)));
            await session.SaveChangesAsync();
        }

        var es = (IEventStore<IDocumentOperations, IQuerySession>)_fixture.Store;
        await es.DeleteProjectionProgressAsync((IEventDatabase)_fixture.Store.Storage.Database, projection,
            tenantId: alpha, CancellationToken.None);

        // Read rows for this test's two tenant identities specifically — the
        // shared store's TripDistance prefix carries sibling tests' rows too,
        // so filter by ":<tenant>" suffix.
        var rows = await _fixture.ReadProgressionRowsAsync(_fixture.SchemaName, projection);
        var ours = rows.Where(r => r.Name.EndsWith(":" + alpha) || r.Name.EndsWith(":" + beta)).ToList();
        ours.Count.ShouldBe(1, "alpha's row was deleted; beta's row should remain.");
        ours[0].Name.ShouldBe(betaName.Identity);
    }

    [Fact]
    public async Task delete_projection_progress_with_null_tenant_id_keeps_legacy_drop_all_behavior()
    {
        // The null-tenant path drops EVERY registered-shard tenantless name for
        // the projection — which means the projection HAS to be registered.
        // Use the fixture's TripDistance and reach for the same TripDistanceProjection
        // shard list. Seeded tenantless row gets dropped; tenant-bearing rows survive.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var projection = TripDistanceProjection.ProjectionName;
        var alphaName = ShardName.Compose(projection, tenantId: alpha);
        var betaName = ShardName.Compose(projection, tenantId: beta);
        var globalName = ShardName.Compose(projection);

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(alphaName, 0, 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(betaName, 0, 20, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(globalName, 0, 5, agent: null!)));
            await session.SaveChangesAsync();
        }

        var es = (IEventStore<IDocumentOperations, IQuerySession>)_fixture.Store;
        await es.DeleteProjectionProgressAsync((IEventDatabase)_fixture.Store.Storage.Database, projection,
            tenantId: null, CancellationToken.None);

        // Read all rows that share the TripDistance prefix. Our tenant-bearing rows
        // for alpha/beta survive (unique tenant ids = unique row names = no overlap
        // with other tests' progression rows); the tenantless row got dropped.
        var rows = await _fixture.ReadProgressionRowsAsync(_fixture.SchemaName, projection);
        rows.Select(r => r.Name).ShouldNotContain(globalName.Identity, "tenantless row gets dropped");
        rows.Select(r => r.Name).ShouldContain(alphaName.Identity, "per-tenant rows survive the legacy delete");
        rows.Select(r => r.Name).ShouldContain(betaName.Identity);
    }
}
