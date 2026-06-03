using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c — inline <c>SingleStreamProjection</c> behavior under
/// <c>UseTenantPartitionedEvents</c>. Pins (a) per-tenant materialization
/// (each tenant's projected doc lives only in its own session view),
/// (b) the silent-split for the same stream id across tenants
/// (each tenant materializes its own distinct snapshot, no doc-state leak),
/// (c) inline snapshots accumulate correctly across multiple SaveChanges
/// per tenant, and (d) the structural invariant that projected document
/// tables are NOT partitioned — only mt_events and mt_streams are.
///
/// <para>
/// Uses the fixture's pre-registered <see cref="TripCountInlineProjection"/>
/// (Inline lifecycle, name = "TripCount", projected to <c>p2c_trip_count</c>).
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class inline_single_stream_projection_per_tenant
{
    private readonly GuidPartitionedFixture _fixture;

    public inline_single_stream_projection_per_tenant(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task inline_projection_materializes_only_in_owning_tenants_session_view()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Alpha appends 3 TripLeg events; beta appends 0.
        var streamId = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession(alpha))
        {
            session.Events.StartStream<TripCount>(streamId, new TripStarted(streamId),
                new TripLeg(1), new TripLeg(2), new TripLeg(3));
            await session.SaveChangesAsync();
        }

        // Alpha sees the projected TripCount(Count=3).
        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaDoc = await qa.LoadAsync<TripCount>(streamId);
        alphaDoc.ShouldNotBeNull();
        alphaDoc!.Count.ShouldBe(3);

        // Beta's session has no doc at this id (multi-tenanted policy +
        // partitioned doc table → tenant-scoped read returns null).
        await using var qb = _fixture.Store.QuerySession(beta);
        var betaDoc = await qb.LoadAsync<TripCount>(streamId);
        betaDoc.ShouldBeNull();
    }

    [Fact]
    public async Task two_tenants_driving_same_stream_id_each_get_their_own_snapshot()
    {
        // Same stream id, two tenants → silent split at the events layer
        // (verified by the AppendWrite tests). Pin that the inline projection
        // honors the split too: each tenant materializes its OWN snapshot
        // independently, no doc-state leak between tenants.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var sharedId = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession(alpha))
        {
            session.Events.StartStream<TripCount>(sharedId, new TripStarted(sharedId),
                new TripLeg(1), new TripLeg(2));
            await session.SaveChangesAsync();
        }
        await using (var session = _fixture.Store.LightweightSession(beta))
        {
            session.Events.StartStream<TripCount>(sharedId, new TripStarted(sharedId),
                new TripLeg(1), new TripLeg(2), new TripLeg(3), new TripLeg(4));
            await session.SaveChangesAsync();
        }

        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaDoc = await qa.LoadAsync<TripCount>(sharedId);
        alphaDoc.ShouldNotBeNull();
        alphaDoc!.Count.ShouldBe(2);

        await using var qb = _fixture.Store.QuerySession(beta);
        var betaDoc = await qb.LoadAsync<TripCount>(sharedId);
        betaDoc.ShouldNotBeNull();
        betaDoc!.Count.ShouldBe(4);
    }

    [Fact]
    public async Task inline_snapshot_accumulates_across_multiple_save_changes_per_tenant()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.StartStream<TripCount>(streamId, new TripStarted(streamId), new TripLeg(1));
            await session.SaveChangesAsync();
        }
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.Append(streamId, new TripLeg(2), new TripLeg(3));
            await session.SaveChangesAsync();
        }
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.Append(streamId, new TripLeg(4));
            await session.SaveChangesAsync();
        }

        await using var query = _fixture.Store.QuerySession(tenant);
        var doc = await query.LoadAsync<TripCount>(streamId);
        doc.ShouldNotBeNull();
        // Inline materialization runs in each SaveChanges' session — Count
        // ends up at 4 (= TripLeg events appended; the initial TripStarted
        // doesn't increment).
        doc!.Count.ShouldBe(4);
    }

    [Fact]
    public async Task projected_document_table_is_NOT_partitioned_by_default()
    {
        // Invariant: UseTenantPartitionedEvents only partitions mt_events and
        // mt_streams. The projected DOCUMENT tables (mt_doc_*) stay as plain
        // conjoined tables — no LIST partition children. This is the structural
        // shape that lets a single migration apply to the doc tables (vs the
        // events tables which need per-tenant attach). Pin the no-partitioning
        // shape so a future change that auto-partitions doc tables is reviewed
        // intentionally.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var tripCountTable = new Table(new PostgresqlObjectName(_fixture.SchemaName, "mt_doc_p2c_trip_count"));
        var live = await tripCountTable.FetchExistingAsync(conn);
        live.ShouldNotBeNull("p2c_trip_count document table must exist after fixture init");

        live.Partitioning.ShouldBeNull(
            "Projected document tables must NOT be partitioned by default — only mt_events / mt_streams are partitioned under UseTenantPartitionedEvents. " +
            "If you intend to also partition document tables, you'd opt in explicitly via PartitionMultiTenantedDocumentsUsingMartenManagement().");
    }
}
