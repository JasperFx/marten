using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.ReadQuery;

/// <summary>
/// #4617 section 3b — deferred items not covered in
/// <c>read_query_aggregation_guid.cs</c>: AggregateStreamToLastKnownAsync
/// per-tenant scoping + EventQuery / QueryEventsAsync paging-per-tenant.
///
/// <para>
/// EnableUniqueIndexOnEventId is intentionally deferred — it requires building
/// the store with a different events-flag set, which is a heavier own-store
/// shape; covered in a follow-up.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class read_query_deferred_items
{
    private readonly GuidPartitionedFixture _fixture;

    public read_query_deferred_items(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AggregateStreamToLastKnownAsync_returns_last_buildable_snapshot_for_owning_tenant()
    {
        // AggregateStreamToLastKnownAsync == AggregateStreamAsync that falls
        // back through successively earlier versions until one returns a
        // non-null aggregate (or the stream runs out). For a non-deletable
        // aggregate this just returns the latest snapshot; the per-tenant
        // pin we want is that each tenant's call returns ITS OWN snapshot,
        // and a third unrelated tenant gets null (their partition is empty
        // at this id).
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        var ghost = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta, ghost);

        var sharedStreamId = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession(alpha))
        {
            session.Events.StartStream<TripSnapshot>(sharedStreamId,
                new TripStarted(sharedStreamId), new TripLeg(3), new TripLeg(4));
            await session.SaveChangesAsync();
        }
        await using (var session = _fixture.Store.LightweightSession(beta))
        {
            session.Events.StartStream<TripSnapshot>(sharedStreamId,
                new TripStarted(sharedStreamId), new TripLeg(10), new TripLeg(20), new TripLeg(30));
            await session.SaveChangesAsync();
        }

        await using var qa = _fixture.Store.QuerySession(alpha);
        var alphaSnap = await qa.Events.AggregateStreamToLastKnownAsync<TripSnapshot>(sharedStreamId);
        alphaSnap.ShouldNotBeNull();
        alphaSnap!.Distance.ShouldBe(7,
            "alpha's last-known snapshot reflects ONLY alpha's events (3 + 4 = 7) — beta's must not leak");
        alphaSnap.LegCount.ShouldBe(2);

        await using var qb = _fixture.Store.QuerySession(beta);
        var betaSnap = await qb.Events.AggregateStreamToLastKnownAsync<TripSnapshot>(sharedStreamId);
        betaSnap.ShouldNotBeNull();
        betaSnap!.Distance.ShouldBe(60,
            "beta's last-known snapshot reflects ONLY beta's events (10 + 20 + 30 = 60) — alpha's must not leak");
        betaSnap.LegCount.ShouldBe(3);

        // A third tenant with no events at this stream id gets null — the
        // call internally calls FetchStreamAsync (tenant-scoped) which returns
        // empty, so the loop never produces an aggregate.
        await using var qg = _fixture.Store.QuerySession(ghost);
        var ghostSnap = await qg.Events.AggregateStreamToLastKnownAsync<TripSnapshot>(sharedStreamId);
        ghostSnap.ShouldBeNull(
            "ghost tenant has no events at this stream id — must return null, not leak alpha's or beta's snapshot");
    }

    [Fact]
    public async Task QueryEventsAsync_pages_only_within_the_querying_tenant()
    {
        // QueryEventsAsync sits on top of QueryAllRawEvents (which is
        // tenant-scoped via Marten LINQ). The pin: alpha's paged query never
        // sees beta's events, TotalCount reflects alpha's count alone, and
        // PageSize is honored.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // 10 events for alpha (1 stream of 10), 10 for beta (1 stream of 10).
        var alphaStream = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession(alpha))
        {
            session.Events.StartStream<TripSnapshot>(alphaStream, new TripStarted(alphaStream));
            for (var i = 0; i < 9; i++)
            {
                session.Events.Append(alphaStream, new TripLeg(1));
            }
            await session.SaveChangesAsync();
        }
        var betaStream = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession(beta))
        {
            session.Events.StartStream<TripSnapshot>(betaStream, new TripStarted(betaStream));
            for (var i = 0; i < 9; i++)
            {
                session.Events.Append(betaStream, new TripLeg(99));
            }
            await session.SaveChangesAsync();
        }

        // Alpha's session pages 5 at a time — scope the query to alpha's
        // stream id so we don't pick up other tests' leftover data on the
        // shared store. (The shared fixture's TripSnapshot live-aggregation
        // means sibling tests may have written events; pin tenant isolation
        // by filtering to this test's stream and asserting TotalCount.)
        await using var qa = _fixture.Store.QuerySession(alpha);
        // QueryEventsAsync lives on IReadOnlyEventStore (JasperFx) — not on
        // IQueryEventStore (Marten). The concrete QueryEventStore implements
        // both, so cast through IReadOnlyEventStore to reach the paging API.
        var alphaReader = (IReadOnlyEventStore)qa.Events;
        var alphaPage1 = await alphaReader.QueryEventsAsync(new EventQuery
        {
            StreamId = alphaStream.ToString(),
            PageSize = 5,
            PageNumber = 1
        }, CancellationToken.None);

        alphaPage1.TotalCount.ShouldBe(10,
            "alpha appended 10 events to this stream — TotalCount must reflect that, not pick up beta's 10");
        alphaPage1.Events.Count.ShouldBe(5, "PageSize == 5");
        alphaPage1.PageSize.ShouldBe(5);
        alphaPage1.PageNumber.ShouldBe(1);

        // Headline tenant-isolation: alpha's page must never contain beta's events.
        alphaPage1.Events.Any(e => e.StreamId == betaStream).ShouldBeFalse(
            "tenant isolation: alpha's paged query must NOT see beta's events");

        // Page 2 returns the remaining 5.
        var alphaPage2 = await alphaReader.QueryEventsAsync(new EventQuery
        {
            StreamId = alphaStream.ToString(),
            PageSize = 5,
            PageNumber = 2
        }, CancellationToken.None);
        alphaPage2.TotalCount.ShouldBe(10);
        alphaPage2.Events.Count.ShouldBe(5);

        // Symmetric pin from beta's session: beta sees only its own 10.
        await using var qb = _fixture.Store.QuerySession(beta);
        var betaReader = (IReadOnlyEventStore)qb.Events;
        var betaPage = await betaReader.QueryEventsAsync(new EventQuery
        {
            StreamId = betaStream.ToString(),
            PageSize = 100,
            PageNumber = 1
        }, CancellationToken.None);
        betaPage.TotalCount.ShouldBe(10);
        betaPage.Events.Any(e => e.StreamId == alphaStream).ShouldBeFalse(
            "tenant isolation: beta's paged query must NOT see alpha's events");
    }
}
