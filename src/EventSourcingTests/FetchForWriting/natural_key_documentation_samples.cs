using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

#region sample_natural_key_registered_order

public class RegisteredOrder
{
    public Guid Id { get; set; }

    [NaturalKey]
    public OrderNumber OrderNum { get; set; } = null!;

    public int Items { get; set; }

    public void Apply(OrderCreated e) => OrderNum = e.OrderNumber;

    public void Apply(OrderItemAdded e) => Items++;

    public void Apply(OrderNumberChanged e) => OrderNum = e.NewOrderNumber;
}

#endregion

#region sample_natural_key_explicit_registration_in_projection_class

public class RegisteredOrderProjection: SingleStreamProjection<RegisteredOrder, Guid>
{
    public RegisteredOrderProjection()
    {
        NaturalKeyFor(x => x
            .SetBy<OrderCreated>(e => e.OrderNumber)
            .SetBy<OrderNumberChanged>(e => e.NewOrderNumber));
    }
}

#endregion

/// <summary>
/// The code samples in docs/events/natural-keys.md that no other test shows.
/// </summary>
public class natural_key_documentation_samples: OneOffConfigurationsContext
{
    private async Task<Guid> startOrder(OrderNumber number)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<OrderAggregate>(streamId, new OrderCreated(number, "Alice"));
        await session.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public async Task a_second_stream_cannot_claim_a_key_in_use()
    {
        StoreOptions(opts => opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline));

        var number = new OrderNumber("ORD-UNIQUE");
        var original = await startOrder(number);
        var claimant = Guid.NewGuid();

        await using var session = theStore.LightweightSession();

        #region sample_natural_key_duplicate_claim

        session.Events.StartStream<OrderAggregate>(claimant, new OrderCreated(number, "Bob"));

        try
        {
            await session.SaveChangesAsync();
        }
        catch (DuplicateNaturalKeyException e)
        {
            // Nothing in the session was saved. The exception says which key was claimed twice,
            // and by which streams
            Console.WriteLine($"{e.Key} already belongs to stream {e.ExistingStreamId}, " +
                              $"so stream {e.ClaimingStreamId} cannot claim it");
        }

        #endregion

        await using var query = theStore.LightweightSession();
        var order = await query.Events.FetchLatest<OrderAggregate, OrderNumber>(number);
        order.ShouldNotBeNull();
        order.Id.ShouldBe(original);
        order.CustomerName.ShouldBe("Alice");
    }

    [Fact]
    public async Task fetch_for_exclusive_writing_by_natural_key()
    {
        StoreOptions(opts => opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline));

        var orderNumber = new OrderNumber("ORD-EXCLUSIVE");
        var streamId = await startOrder(orderNumber);

        await using (var session = theStore.LightweightSession())
        {
            #region sample_fetch_for_exclusive_writing_by_natural_key

            // Locks the stream until the session is saved or disposed
            var stream = await session.Events.FetchForExclusiveWriting<OrderAggregate, OrderNumber>(orderNumber);

            stream.AppendOne(new OrderItemAdded("Gadget", 19.99m));
            await session.SaveChangesAsync();

            #endregion
        }

        await using var query = theStore.LightweightSession();
        var order = await query.Events.FetchLatest<OrderAggregate>(streamId);
        order.ShouldNotBeNull();
        order.TotalAmount.ShouldBe(19.99m);
    }

    [Fact]
    public async Task natural_key_mappings_registered_on_the_projection()
    {
        StoreOptions(opts =>
        {
            #region sample_natural_key_explicit_registration

            opts.Projections.Snapshot<RegisteredOrder>(SnapshotLifecycle.Inline, p =>
                ((SingleStreamProjection<RegisteredOrder, Guid>)p).NaturalKeyFor(x => x
                    // Read the key from the event body
                    .SetBy<OrderCreated>(e => e.OrderNumber)
                    // ...or from the whole event, when the key depends on its metadata, such as the
                    // stream key, timestamp or headers
                    .SetByEvent<OrderNumberChanged>(e => e.Data.NewOrderNumber)));

            #endregion
        });

        await renumberAndFetch();
    }

    [Fact]
    public async Task natural_key_mappings_registered_in_a_projection_class()
    {
        StoreOptions(opts => opts.Projections.Add<RegisteredOrderProjection>(ProjectionLifecycle.Inline));

        await renumberAndFetch();
    }

    private async Task renumberAndFetch()
    {
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<RegisteredOrder>(streamId, new OrderCreated(new OrderNumber("ORD-OLD"), "Alice"),
                new OrderNumberChanged(new OrderNumber("ORD-NEW")));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchLatest<RegisteredOrder, OrderNumber>(new OrderNumber("ORD-NEW")))!.Id
            .ShouldBe(streamId);
        (await query.Events.FetchLatest<RegisteredOrder, OrderNumber>(new OrderNumber("ORD-OLD"))).ShouldBeNull();
    }
}
