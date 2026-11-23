using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// A Live aggregate fetched by natural key reads the stream's version, then its events with no upper bound, so an
/// append committed between the two reads is among the events. The fetch has to answer at that event's version, as
/// FetchLivePlan does by stream id: labelled with the version read first, the aggregate claims fewer events than it
/// folded, so its own commit fails against an append it already accounts for, and the write cache keeps it a version
/// behind and folds that event onto it a second time.
/// </summary>
public class live_fetch_by_natural_key_reads_in_a_safe_order: OneOffConfigurationsContext
{
    private void configure(bool cache)
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<OrderAggregate>();
            opts.Events.UseIdentityMapForAggregates = false;
            if (cache) opts.Events.CacheAggregatesForWriting<OrderAggregate>();
        });
    }

    private async Task<Guid> startOrderAsync(OrderNumber number)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(number, "Alice"),
            new OrderItemAdded("Widget", 1m));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return streamId;
    }

    private async Task addAnItemAsync(Guid streamId)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, new OrderItemAdded("Gadget", 10m));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async_fetch_reads_in_a_safe_order.PauseTheFirstRead pauseAndAddAnItem(Guid streamId) =>
        new(() => addAnItemAsync(streamId), "mt_doc_orderaggregate", streamVersionRead: true);

    [Fact]
    public async Task an_append_between_the_reads_is_counted_and_the_fetch_answers_at_its_version()
    {
        configure(cache: false);
        var number = new OrderNumber("LIVE-RACE");
        var streamId = await startOrderAsync(number);

        await using var session = theStore.LightweightSession();
        var pause = pauseAndAddAnItem(streamId);
        session.Logger = pause;

        var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(number,
            TestContext.Current.CancellationToken);
        await pause.Paused;

        stream.Aggregate!.TotalAmount.ShouldBe(11m);
        stream.CurrentVersion.ShouldBe(3);

        // The aggregate already accounts for the item appended between the reads, so its own commit is not a conflict
        stream.AppendOne(new OrderItemAdded("Sprocket", 100m));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var query = theStore.QuerySession();
        var state = await query.Events.FetchStreamStateAsync(streamId, TestContext.Current.CancellationToken);
        state!.Version.ShouldBe(4);
    }

    [Fact]
    public async Task the_write_cache_holds_the_aggregate_at_the_version_it_folded_to()
    {
        configure(cache: true);
        var number = new OrderNumber("LIVE-CACHED");
        var streamId = await startOrderAsync(number);

        // Nothing is cached yet, so this fetch reads every event, and the append lands between its reads
        await using (var session = theStore.LightweightSession())
        {
            var pause = pauseAndAddAnItem(streamId);
            session.Logger = pause;

            var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(number,
                TestContext.Current.CancellationToken);
            await pause.Paused;

            stream.Aggregate!.TotalAmount.ShouldBe(11m);
            stream.CurrentVersion.ShouldBe(3);

            // A commit this session owns writes the aggregate back to the cache
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Cached at version 3, the item appended between the reads is not folded onto it again
        await using var next = theStore.LightweightSession();
        var again = await next.Events.FetchForWriting<OrderAggregate, OrderNumber>(number,
            TestContext.Current.CancellationToken);

        again.Aggregate!.TotalAmount.ShouldBe(11m);
        again.CurrentVersion.ShouldBe(3);
    }
}
