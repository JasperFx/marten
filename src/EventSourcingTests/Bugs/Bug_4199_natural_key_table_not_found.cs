using System;
using System.Threading.Tasks;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_4199_natural_key_table_not_found : OneOffConfigurationsContext
{
    public sealed record OrderNumber(string Value);

    public sealed record OrderPlaced(Guid OrderId, string OrderNumber);

    public sealed record OrderShipped(Guid OrderId, string TrackingNumber);

    public sealed class OrderAggregate
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public OrderNumber Number { get; set; }

        public string? TrackingNumber { get; set; }

        [NaturalKeySource]
        public void Apply(OrderPlaced e)
        {
            Id = e.OrderId;
            Number = new OrderNumber(e.OrderNumber);
        }

        public void Apply(OrderShipped e)
        {
            TrackingNumber = e.TrackingNumber;
        }
    }

    [Fact]
    public async Task should_auto_create_natural_key_table_on_fetch_for_writing()
    {
        // This is the exact scenario from issue #4199:
        // No explicit projection registration, no ApplyAllConfiguredChangesToDatabaseAsync(),
        // just FetchForWriting with a natural key type on a self-aggregating aggregate.
        // The natural key table should be auto-created.
        StoreOptions(opts =>
        {
            // Deliberately no projection registration - relying on auto-discovery
        });

        var orderId = Guid.NewGuid();
        var orderNumber = new OrderNumber("ORD-12345");

        // Trigger auto-discovery of the natural key projection by calling FetchForWriting.
        // This is the pattern the user would follow: first attempt triggers registration,
        // then subsequent writes include the inline projection.
        await using var session0 = theStore.LightweightSession();
        var preCheck = await session0.Events.FetchForWriting<OrderAggregate, OrderNumber>(
            new OrderNumber("nonexistent"));
        preCheck.Aggregate.ShouldBeNull(); // No stream exists yet, that's fine

        // Now start a stream — the inline projection is registered, so the natural key
        // mapping will be written alongside the events
        await using var session1 = theStore.LightweightSession();
        session1.Events.StartStream<OrderAggregate>(orderId,
            new OrderPlaced(orderId, orderNumber.Value));
        await session1.SaveChangesAsync();

        // Fetch by natural key — should find the aggregate
        await using var session2 = theStore.LightweightSession();
        var stream = await session2.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);

        stream.ShouldNotBeNull();
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Number.ShouldBe(orderNumber);
        stream.Aggregate.Id.ShouldBe(orderId);
    }

    [Fact]
    public async Task should_work_with_explicit_inline_projection()
    {
        // Verify the explicit registration path still works
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderId = Guid.NewGuid();
        var orderNumber = new OrderNumber("ORD-67890");

        await using var session1 = theStore.LightweightSession();
        session1.Events.StartStream<OrderAggregate>(orderId,
            new OrderPlaced(orderId, orderNumber.Value));
        await session1.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        var stream = await session2.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);

        stream.ShouldNotBeNull();
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Number.ShouldBe(orderNumber);
    }
}
