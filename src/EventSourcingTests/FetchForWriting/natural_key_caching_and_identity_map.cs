using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// Natural key fetches use the aggregate write cache and the identity map, as fetches by stream id do. The
/// cache is found through a pointer from the key to the stream id, which the database confirms in the same
/// round trip; the identity map is searched by the aggregates' current key values.
/// </summary>
public class natural_key_caching_and_identity_map: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    // UseIdentityMapForAggregates is left at its default, true. With it off the Inline write-back caches an
    // aggregate the commit never updated (see the notes), which is not what these tests are about.
    private void CacheOrders()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<OrderAggregate>();
            opts.Events.CacheAggregatesForWriting<OrderSummary>();
        });
    }

    private async Task<Guid> startOrder(OrderNumber number)
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId, new OrderCreated(number, "Alice"),
            new OrderItemAdded("Widget", 10m));
        await theSession.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public async Task an_async_aggregate_is_taken_from_the_cache_by_its_mapped_key()
    {
        CacheOrders();
        var number = new OrderNumber("CACHE-ASYNC");
        var streamId = await startOrder(number);

        // The commit writes the aggregate back to the cache at the version it was fetched at, and points its
        // keys at it
        await using (var first = theStore.LightweightSession())
        {
            var fetched = await first.Events.FetchForWriting<OrderSummary, OrderNumber>(number);
            fetched.Aggregate!.Items.ShouldBe(1);
            fetched.AppendOne(new OrderItemAdded("Gizmo", 1m));
            await first.SaveChangesAsync();
        }

        theSession.Events.Append(streamId, new OrderItemAdded("Gadget", 5m));
        await theSession.SaveChangesAsync();
        var hits = theCache.Hits;

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<OrderSummary, OrderNumber>(number);

        theCache.Hits.ShouldBe(hits + 1);
        stream.Id.ShouldBe(streamId);
        stream.CurrentVersion.ShouldBe(4);

        // The cached baseline, plus the two events appended since it was fetched
        stream.Aggregate!.Items.ShouldBe(3);
    }

    [Fact]
    public async Task the_identity_map_is_searched_by_the_current_key()
    {
        CacheOrders();
        var original = new OrderNumber("MAP-OLD");
        var renamed = new OrderNumber("MAP-NEW");
        var streamId = await startOrder(original);

        await using var session = theStore.IdentitySession();
        var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(original);
        stream.AppendOne(new OrderNumberChanged(renamed));
        await session.SaveChangesAsync();

        // The aggregate in the map now carries the new key, so the old one is not answered from it
        (await session.Events.FetchLatest<OrderAggregate, OrderNumber>(original)).ShouldBeNull();
        (await session.Events.FetchLatest<OrderAggregate, OrderNumber>(renamed))!.Id.ShouldBe(streamId);
    }

    public enum FoldedLifecycle
    {
        Async,
        Live
    }

    [Theory]
    [InlineData(FoldedLifecycle.Async, false)]
    [InlineData(FoldedLifecycle.Async, true)]
    [InlineData(FoldedLifecycle.Live, false)]
    [InlineData(FoldedLifecycle.Live, true)]
    public async Task events_appended_since_a_fetch_are_folded_once(FoldedLifecycle lifecycle, bool thenByStreamId)
    {
        StoreOptions(opts =>
        {
            if (lifecycle == FoldedLifecycle.Async)
            {
                opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Async);
            }
            else
            {
                opts.Projections.LiveStreamAggregation<OrderAggregate>();
            }

            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<OrderAggregate>();
        });

        var number = new OrderNumber($"FOLD-{lifecycle}-{thenByStreamId}");
        var streamId = await startOrder(number);

        await using (var session = theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);
            stream.AppendOne(new OrderItemAdded("Gizmo", 5m));

            // Answered from the identity map, with the pending event folded onto the fetched aggregate
            (await session.Events.FetchLatest<OrderAggregate, OrderNumber>(number))!.TotalAmount.ShouldBe(15m);

            // The fold leaves the stream out of the identity map, as a FetchLatest by stream id does, so the next
            // fetch reads the committed stream rather than folding the pending event a second time
            var again = thenByStreamId
                ? await session.Events.FetchLatest<OrderAggregate>(streamId)
                : await session.Events.FetchLatest<OrderAggregate, OrderNumber>(number);
            again!.Id.ShouldBe(streamId);
            again.TotalAmount.ShouldBe(10m);

            await session.SaveChangesAsync();
        }

        // Nor is the folded aggregate written back to the cache under the version it was fetched at
        await using var after = theStore.LightweightSession();
        var fetched = await after.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);
        fetched.CurrentVersion.ShouldBe(3);
        fetched.Aggregate!.TotalAmount.ShouldBe(15m);
    }
}
