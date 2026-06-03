using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Progress;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_progression_keying.cs
/// — #4596 Phase 1 Session 3: per-tenant <c>mt_event_progression</c> keying
/// via <see cref="ShardName.Identity"/> rather than a separate tenant_id column.
/// The PK stays just <c>(name)</c>; per-tenant rows are distinguished because
/// jasperfx#407 Phase 0's <see cref="ShardName.Compose"/> folds the optional
/// tenant slot into the identity (<c>{Name}:{ShardKey}:{tenantId}</c>).
///
/// <para>
/// These four tests are the CLEAN per-tenant ones that just need the shared
/// store's progression table — per-test isolation comes from unique
/// <see cref="PartitionedFixtureBase.NewTenant"/> ids. The fifth schema-shape
/// test that depended on a tenantless probe lives in
/// <see cref="Config.event_progression_schema_layout"/>.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class per_tenant_progression_keying
{
    private readonly GuidPartitionedFixture _fixture;

    public per_tenant_progression_keying(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task tenant_bearing_shard_names_produce_independent_progression_rows()
    {
        // Per-test unique tenant ids = unique row names = no overlap with
        // sibling tests on the shared store.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // ShardName.Compose with explicit tenant slot — jasperfx#407 Phase 0
        // surfaces tenant as a distinct part of the shard grammar that
        // serializes into Identity as `{Name}:{ShardKey}:{tenantId}`.
        var projection = "orders_projection_" + alpha;
        var alphaName = ShardName.Compose(projection, tenantId: alpha);
        var betaName = ShardName.Compose(projection, tenantId: beta);

        alphaName.Identity.ShouldBe($"{projection}:All:{alpha}",
            "ShardName.Compose with a tenant id produces a 3-segment identity.");
        betaName.Identity.ShouldBe($"{projection}:All:{beta}");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(alphaName, floor: 0, ceiling: 17, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(betaName, floor: 0, ceiling: 42, agent: null!)));
            await session.SaveChangesAsync();
        }

        // The PK is single-column (name); the two ShardName identities are
        // distinct strings; therefore two independent rows. Filter by the
        // unique projection prefix so sibling tests' rows don't pollute the read.
        var rows = await _fixture.ReadProgressionRowsAsync(_fixture.SchemaName, projection);
        rows.Count.ShouldBe(2);
        rows.Single(r => r.Name == alphaName.Identity).LastSeqId.ShouldBe(17L);
        rows.Single(r => r.Name == betaName.Identity).LastSeqId.ShouldBe(42L);
    }

    [Fact]
    public async Task tenantless_shard_name_writes_a_single_row_with_the_legacy_identity()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        // Unique projection name keeps this test's tenantless row from
        // colliding with sibling tests' progression rows on the shared store.
        var projection = "orders_projection_global_" + tenant;
        // No tenant slot → Identity is `{Name}:{ShardKey}` exactly like every
        // pre-#4596 store. Single store-global row.
        var globalName = ShardName.Compose(projection);
        globalName.Identity.ShouldBe($"{projection}:All");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(tenant))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(globalName, floor: 0, ceiling: 99, agent: null!)));
            await session.SaveChangesAsync();
        }

        var rows = await _fixture.ReadProgressionRowsAsync(_fixture.SchemaName, projection);
        rows.Count.ShouldBe(1);
        rows[0].Name.ShouldBe(globalName.Identity);
        rows[0].LastSeqId.ShouldBe(99L);
    }

    [Fact]
    public async Task per_tenant_update_only_touches_that_tenants_row()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var projection = "orders_projection_update_" + alpha;
        var alphaName = ShardName.Compose(projection, tenantId: alpha);
        var betaName = ShardName.Compose(projection, tenantId: beta);

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(alphaName, floor: 0, ceiling: 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(betaName, floor: 0, ceiling: 20, agent: null!)));
            await session.SaveChangesAsync();
        }

        // Bump only alpha's row from 10 → 25. Because the `name` column carries
        // the tenant suffix, WHERE name = '<projection>:All:<alpha>' naturally
        // scopes to one tenant.
        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            session.QueueOperation(new UpdateProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(alphaName, floor: 10, ceiling: 25, agent: null!)));
            await session.SaveChangesAsync();
        }

        var rows = await _fixture.ReadProgressionRowsAsync(_fixture.SchemaName, projection);
        rows.Single(r => r.Name == alphaName.Identity).LastSeqId.ShouldBe(25L);
        rows.Single(r => r.Name == betaName.Identity).LastSeqId.ShouldBe(20L,
            "beta's row must be unaffected by alpha's update — the per-tenant suffix on the name column does the scoping.");
    }

    [Fact]
    public async Task per_tenant_delete_only_drops_that_tenants_row()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var projection = "orders_projection_delete_" + alpha;
        var alphaName = ShardName.Compose(projection, tenantId: alpha);
        var betaName = ShardName.Compose(projection, tenantId: beta);

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(alphaName, floor: 0, ceiling: 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(_fixture.Store.Options.EventGraph,
                new EventRange(betaName, floor: 0, ceiling: 20, agent: null!)));
            await session.SaveChangesAsync();
        }

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)_fixture.Store.LightweightSession(alpha))
        {
            // Pass the tenant-bearing ShardName.Identity — the existing
            // single-arg DeleteProjectionProgress already scopes correctly
            // because the name column carries the tenant suffix.
            session.QueueOperation(new DeleteProjectionProgress(_fixture.Store.Options.EventGraph, alphaName.Identity));
            await session.SaveChangesAsync();
        }

        var rows = await _fixture.ReadProgressionRowsAsync(_fixture.SchemaName, projection);
        rows.Count.ShouldBe(1);
        rows[0].Name.ShouldBe(betaName.Identity);
        rows[0].LastSeqId.ShouldBe(20L);
    }
}
