using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using JasperFx;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class natural_key_expected_version: OneOffConfigurationsContext
{
    private void configure(bool live = false, bool cache = false)
    {
        StoreOptions(opts =>
        {
            if (live) opts.Projections.LiveStreamAggregation<OrderAggregate>();
            else opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
            if (cache) opts.Events.CacheAggregatesForWriting<OrderAggregate>();
        });
    }

    private async Task<Guid> startOrder(OrderNumber number, int items)
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId, new OrderCreated(number, "Alice"));
        for (var i = 0; i < items; i++)
        {
            theSession.Events.Append(streamId, new OrderItemAdded("Widget", 10m));
        }

        await theSession.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public async Task fetch_for_writing_by_natural_key_at_the_expected_version()
    {
        configure();

        var order = new OrderNumber("EV-1");
        var orderId = await startOrder(order, 2);

        await using var session = theStore.LightweightSession();

        #region sample_fetch_for_writing_by_natural_key_with_expected_version
        var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(order, 3);
        #endregion

        stream.Id.ShouldBe(orderId);
        stream.CurrentVersion.ShouldBe(3);

        stream.AppendOne(new OrderItemAdded("Gadget", 5m));
        await session.SaveChangesAsync();

        (await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(order))!.TotalAmount.ShouldBe(25m);
    }

    [Fact]
    public async Task an_unknown_natural_key_is_at_version_zero()
    {
        configure();

        await using var session = theStore.LightweightSession();

        var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(new OrderNumber("EV-NONE"), 0);
        stream.CurrentVersion.ShouldBe(0);
        stream.Aggregate.ShouldBeNull();

        await Should.ThrowAsync<ConcurrencyException>(() =>
            session.Events.FetchForWriting<OrderAggregate, OrderNumber>(new OrderNumber("EV-NONE"), 1));
    }

    public static TheoryData<bool, bool> EveryShape() => new()
    {
        { false, false }, { false, true }, { true, false }, { true, true }
    };

    [Theory]
    [MemberData(nameof(EveryShape))]
    public async Task a_stale_version_leaves_the_session_as_it_was(bool live, bool cache)
    {
        configure(live, cache);

        var order = new OrderNumber("EV-STALE");
        var orderId = await startOrder(order, 2);

        await using var session = theStore.LightweightSession();

        // Checked before the fetch enlists anything, as a fetch by stream id is
        await Should.ThrowAsync<ConcurrencyException>(() =>
            session.Events.FetchForWriting<OrderAggregate, OrderNumber>(order, 2));

        session.PendingChanges.Streams().ShouldBeEmpty();

        // So the session carries on as though the stale fetch never happened
        var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(order, 3);
        stream.AppendOne(new OrderItemAdded("Gadget", 5m));
        await session.SaveChangesAsync();

        var state = await theSession.Events.FetchStreamStateAsync(orderId);
        state!.Version.ShouldBe(4);
    }

    [Theory]
    [MemberData(nameof(EveryShape))]
    public async Task a_stale_version_in_a_batch_leaves_the_session_as_it_was(bool live, bool cache)
    {
        configure(live, cache);

        var order = new OrderNumber("EV-STALE-BATCH");
        await startOrder(order, 2);

        await using var session = theStore.LightweightSession();

        var batch = session.CreateBatchQuery();
        var fetch = batch.Events.FetchForWriting<OrderAggregate, OrderNumber>(order, 2);

        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await batch.Execute();
            await fetch;
        });

        session.PendingChanges.Streams().ShouldBeEmpty();
    }
}
