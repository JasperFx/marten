using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class natural_key_fetch_many_for_writing: OneOffConfigurationsContext
{
    private void configure()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
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
    public async Task fetches_many_by_natural_key_in_one_round_trip()
    {
        configure();

        var first = new OrderNumber("MANY-1");
        var second = new OrderNumber("MANY-2");
        var firstId = await startOrder(first, 1);
        var secondId = await startOrder(second, 2);

        await using var session = theStore.LightweightSession();
        var before = session.RequestCount;

        #region sample_fetch_many_for_writing_by_natural_key
        var streams = await session.Events.FetchManyForWriting<OrderAggregate, OrderNumber>([first, second]);
        #endregion

        session.RequestCount.ShouldBe(before + 1);

        streams[0].Id.ShouldBe(firstId);
        streams[0].Aggregate!.TotalAmount.ShouldBe(10m);
        streams[1].Id.ShouldBe(secondId);
        streams[1].Aggregate!.TotalAmount.ShouldBe(20m);

        // Each handle keeps its own starting version
        streams[0].AppendOne(new OrderItemAdded("Gadget", 5m));
        streams[1].AppendOne(new OrderItemAdded("Gadget", 5m));
        await session.SaveChangesAsync();

        (await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(first))!.TotalAmount.ShouldBe(15m);
        (await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(second))!.TotalAmount.ShouldBe(25m);
    }

    [Fact]
    public async Task stream_ids_work_through_the_generic_form()
    {
        configure();

        var firstId = await startOrder(new OrderNumber("MANY-ID-1"), 1);
        var secondId = await startOrder(new OrderNumber("MANY-ID-2"), 2);

        await using var session = theStore.LightweightSession();
        var streams = await session.Events.FetchManyForWriting<OrderAggregate, Guid>([firstId, secondId]);

        streams[0].Aggregate!.TotalAmount.ShouldBe(10m);
        streams[1].Aggregate!.TotalAmount.ShouldBe(20m);
    }

    [Fact]
    public async Task an_empty_list_fetches_nothing()
    {
        configure();

        await using var session = theStore.LightweightSession();
        var before = session.RequestCount;

        (await session.Events.FetchManyForWriting<OrderAggregate, OrderNumber>([])).ShouldBeEmpty();
        session.RequestCount.ShouldBe(before);
    }

    [Fact]
    public async Task a_repeated_key_is_refused()
    {
        configure();

        await using var session = theStore.LightweightSession();
        var key = new OrderNumber("MANY-TWICE");

        var ex = await Should.ThrowAsync<ArgumentException>(() =>
            session.Events.FetchManyForWriting<OrderAggregate, OrderNumber>([key, key]));
        ex.Message.ShouldContain("more than once");
    }

    [Fact]
    public async Task a_null_key_is_refused()
    {
        configure();

        await using var session = theStore.LightweightSession();

        var ex = await Should.ThrowAsync<ArgumentNullException>(() =>
            session.Events.FetchManyForWriting<OrderAggregate, OrderNumber>([new OrderNumber("MANY-OK"), null!]));
        ex.Message.ShouldContain("position 1");
    }

    [Fact]
    public async Task the_nullable_key_type_is_refused_even_for_an_empty_list()
    {
        configure();

        await using var session = theStore.LightweightSession();

        await Should.ThrowAsync<ArgumentException>(() =>
            session.Events.FetchManyForWriting<OrderAggregate, Guid?>([]));
    }
}
