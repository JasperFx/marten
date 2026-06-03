#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// #4617 section 4 — fills in the remaining daemon behaviors under
/// <c>UseTenantPartitionedEvents</c> that aren't already covered by
/// <see cref="per_tenant_rebuild_isolation"/> /
/// <see cref="per_tenant_progression_keying"/> /
/// <see cref="event_loader_tenant_filter"/>. Three orthogonal pins:
///
/// <list type="bullet">
/// <item><see cref="RebuildProjectionAsync_for_tenant_materializes_docs_for_that_tenant_only"/>
/// — per-tenant rebuild fans out events for ONE tenant; the sibling tenant
/// stays at its pre-rebuild state. Headline daemon-side complement to the
/// progression-row-only assertion that the sibling
/// <see cref="per_tenant_rebuild_isolation"/> pins.</item>
/// <item><see cref="RebuildProjectionAsync_for_tenant_with_zero_events_is_noop_no_exception"/>
/// — registering a tenant + immediately rebuilding (no events) must NOT throw
/// any "no rows / cannot rebuild" exception; the rebuild path is genuinely
/// resilient to an empty per-tenant event stream.</item>
/// <item><see cref="RebuildProjectionAsync_for_tenant_writes_per_tenant_progression_row"/>
/// — the per-tenant rebuild path writes a
/// <c>{name}:All:{tenant}</c> progression row, NOT the store-global
/// <c>{name}:All</c> catch-up row. Pins the (name, tenant_id) progression
/// keying contract end-to-end via the rebuild executor (the lower-level
/// version of this pin already lives in
/// <see cref="per_tenant_progression_keying"/> which exercises the
/// <c>InsertProjectionProgress</c> operation directly).</item>
/// </list>
///
/// <para>
/// Shared <see cref="GuidPartitionedFixture"/> + unique tenant ids per test —
/// per the fixture contract. Per-tenant rebuilds (over
/// <c>daemon.StartAllAsync()</c> + <c>WaitForNonStaleData()</c>) so the
/// store-global <c>TripDistance:All</c> catch-up row's running watermark
/// (advanced by sibling tests on the shared fixture) doesn't make this test's
/// catch-up a no-op.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class daemon_in_depth_under_partitioning
{
    private readonly GuidPartitionedFixture _fixture;

    public daemon_in_depth_under_partitioning(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RebuildProjectionAsync_for_tenant_materializes_docs_for_that_tenant_only()
    {
        // Seed two tenants on the shared fixture; rebuild only alpha. The
        // headline guarantee is that the per-tenant rebuild path materializes
        // exactly the rebuilt tenant's docs without touching siblings.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Seed events for both; the projection is registered Async so the
        // events sit unprojected until a daemon rebuild fans them out.
        var alphaStreamId = await _fixture.AppendNEventsAsync(alpha, 3);
        var betaStreamId = await _fixture.AppendNEventsAsync(beta, 5);

        using (var daemon = await _fixture.Store.BuildProjectionDaemonAsync())
        {
            // Only alpha is rebuilt — beta's events stay unprojected.
            await daemon.RebuildProjectionAsync(
                TripDistanceProjection.ProjectionName, alpha, CancellationToken.None);
        }

        // alpha's doc lands (3 events: 1 StartStream + 2 TripLeg(1.0) = 2.0 distance).
        await using (var session = _fixture.Store.QuerySession(alpha))
        {
            var alphaDoc = await session.LoadAsync<TripDistance>(alphaStreamId);
            alphaDoc.ShouldNotBeNull(
                "alpha's projection doc must materialize after the per-tenant rebuild");
            alphaDoc.Distance.ShouldBe(2.0,
                "alpha appended 3 events: StartStream + 2x TripLeg(1.0) = 2.0 total distance");
        }

        // beta's doc must NOT exist — its events were never replayed.
        await using (var session = _fixture.Store.QuerySession(beta))
        {
            var betaDoc = await session.LoadAsync<TripDistance>(betaStreamId);
            betaDoc.ShouldBeNull(
                "beta's events must stay unprojected — the rebuild was scoped to alpha");
        }
    }

    [Fact]
    public async Task RebuildProjectionAsync_for_tenant_with_zero_events_is_noop_no_exception()
    {
        // Register a tenant but append nothing. The per-tenant rebuild path
        // must walk zero events without raising "no events to rebuild" or
        // hitting the MT002 / cold-cache guards. This pins the resilience of
        // the rebuild executor against a freshly-registered tenant.
        var emptyTenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, emptyTenant);

        using (var daemon = await _fixture.Store.BuildProjectionDaemonAsync())
        {
            // Should NOT throw — this is the contract we're pinning.
            await daemon.RebuildProjectionAsync(
                TripDistanceProjection.ProjectionName, emptyTenant, CancellationToken.None);
        }

        // No events → no docs. Belt-and-braces probe that the rebuild didn't
        // somehow materialize a tombstone or empty aggregate.
        await using (var session = _fixture.Store.QuerySession(emptyTenant))
        {
            var anyDocs = await session.Query<TripDistance>().AnyAsync();
            anyDocs.ShouldBeFalse(
                "a no-event rebuild must materialize zero docs — not a placeholder or tombstone");
        }
    }

    [Fact]
    public async Task RebuildProjectionAsync_for_tenant_writes_per_tenant_progression_row()
    {
        // Pins the per-tenant rebuild path's progression-row contract:
        // RebuildProjectionAsync(name, tenantId) writes the progression at the
        // tenant-suffixed identity `{name}:All:{tenant}` (jasperfx#407 Phase 0
        // ShardName grammar) — NOT the store-global `{name}:All` identity that
        // the continuous catch-up uses. This is the end-to-end complement to
        // per_tenant_progression_keying's direct-operation tests.
        //
        // NOTE: deliberately tests the REBUILD path (per-tenant entry) rather
        // than continuous-mode StartAgentAsync — the daemon's StartAgentAsync
        // rejects tenant-bearing shard identities ("Unknown shard name") because
        // continuous mode runs a single agent at the tenantless shard identity
        // and fans out internally via the vectorized high-water detector. Only
        // the rebuild path takes a tenant slot as a first-class parameter.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Seed events for both so we can also assert beta's per-tenant
        // progression row stays absent (it was never rebuilt).
        await _fixture.AppendNEventsAsync(alpha, 4);
        await _fixture.AppendNEventsAsync(beta, 2);

        using (var daemon = await _fixture.Store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(
                TripDistanceProjection.ProjectionName, alpha, CancellationToken.None);
        }

        var alphaIdentity = ShardName.Compose(TripDistanceProjection.ProjectionName, tenantId: alpha).Identity;
        var betaIdentity = ShardName.Compose(TripDistanceProjection.ProjectionName, tenantId: beta).Identity;

        // Use the fixture's prefix-filtered reader to avoid sibling tests'
        // tenant-bearing rows on the shared store. Filter the result down to
        // OUR test's tenant ids — they're unique per test, so this scopes
        // cleanly even when other tests have written their own per-tenant rows.
        var allTripDistanceRows = await _fixture.ReadProgressionRowsAsync(
            _fixture.SchemaName, TripDistanceProjection.ProjectionName);

        var alphaRow = allTripDistanceRows.FirstOrDefault(r => r.Name == alphaIdentity);
        var betaRow = allTripDistanceRows.FirstOrDefault(r => r.Name == betaIdentity);

        alphaRow.Name.ShouldNotBeNull(
            $"per-tenant rebuild for alpha must write a progression row at '{alphaIdentity}'");
        alphaRow.LastSeqId.ShouldBeGreaterThanOrEqualTo(4L,
            "alpha appended 4 events — its per-tenant progression row must reflect that high-water");

        betaRow.Name.ShouldBeNull(
            "beta was NOT rebuilt — its per-tenant progression row must NOT exist (it would only be written by a beta-scoped rebuild)");
    }
}
