using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

#region sample_natural_key_aggregate_types

public record OrderNumber(string Value);

public record InvoiceNumber(string Value);

public class OrderAggregate
{
    public Guid Id { get; set; }

    [NaturalKey]
    public OrderNumber OrderNum { get; set; }

    public decimal TotalAmount { get; set; }
    public string CustomerName { get; set; }
    public bool IsComplete { get; set; }

    [NaturalKeySource]
    public void Apply(OrderCreated e)
    {
        OrderNum = e.OrderNumber;
        CustomerName = e.CustomerName;
    }

    public void Apply(OrderItemAdded e)
    {
        TotalAmount += e.Price;
    }

    [NaturalKeySource]
    public void Apply(OrderNumberChanged e)
    {
        OrderNum = e.NewOrderNumber;
    }

    public void Apply(OrderCompleted e)
    {
        IsComplete = true;
    }
}

public class OrderAggregateAsString
{
    public string Id { get; set; }

    [NaturalKey]
    public OrderNumber OrderNum { get; set; }

    public decimal TotalAmount { get; set; }
    public string CustomerName { get; set; }

    [NaturalKeySource]
    public void Apply(OrderCreated e)
    {
        OrderNum = e.OrderNumber;
        CustomerName = e.CustomerName;
    }

    public void Apply(OrderItemAdded e)
    {
        TotalAmount += e.Price;
    }

    [NaturalKeySource]
    public void Apply(OrderNumberChanged e)
    {
        OrderNum = e.NewOrderNumber;
    }
}

public class InvoiceAggregate
{
    public Guid Id { get; set; }

    [NaturalKey]
    public InvoiceNumber InvoiceCode { get; set; }

    public decimal Amount { get; set; }

    [NaturalKeySource]
    public void Apply(InvoiceCreated e)
    {
        InvoiceCode = e.Code;
        Amount = e.Amount;
    }
}

public record OrderCreated(OrderNumber OrderNumber, string CustomerName);
public record OrderItemAdded(string ItemName, decimal Price);
public record OrderNumberChanged(OrderNumber NewOrderNumber);
public record OrderCompleted;
public record InvoiceCreated(InvoiceNumber Code, decimal Amount);

#endregion

public class fetching_by_natural_key: OneOffConfigurationsContext
{
    #region Guid Stream Identity + Inline Lifecycle

    [Fact]
    public async Task fetch_for_writing_new_stream_by_natural_key_returns_null_aggregate()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-999");

        var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);

        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);
    }

    [Fact]
    public async Task fetch_for_writing_existing_stream_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-001");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Alice"),
            new OrderItemAdded("Widget", 9.99m));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.OrderNum.ShouldBe(orderNumber);
        stream.Aggregate.CustomerName.ShouldBe("Alice");
        stream.Aggregate.TotalAmount.ShouldBe(9.99m);
        stream.CurrentVersion.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_for_writing_and_append_events_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-002");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Bob"));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(1);

        stream.AppendOne(new OrderItemAdded("Gadget", 19.99m));
        stream.AppendOne(new OrderItemAdded("Doohickey", 5.50m));
        stream.AppendOne(new OrderCompleted());
        await theSession.SaveChangesAsync();

        // Verify final state by fetching again
        using var verifySession = theStore.LightweightSession();
        var verify = await verifySession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        verify.Aggregate.ShouldNotBeNull();
        verify.Aggregate.TotalAmount.ShouldBe(25.49m);
        verify.Aggregate.IsComplete.ShouldBeTrue();
        verify.CurrentVersion.ShouldBe(4);
    }

    [Fact]
    public async Task fetch_latest_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-003");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Charlie"),
            new OrderItemAdded("Thingamajig", 15.00m));
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);

        aggregate.ShouldNotBeNull();
        aggregate.OrderNum.ShouldBe(orderNumber);
        aggregate.CustomerName.ShouldBe("Charlie");
        aggregate.TotalAmount.ShouldBe(15.00m);
    }

    [Fact]
    public async Task fetch_latest_returns_null_for_nonexistent_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var nonExistentKey = new OrderNumber("ORD-DOES-NOT-EXIST");

        var aggregate = await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(nonExistentKey);

        aggregate.ShouldBeNull();
    }

    [Fact]
    public async Task fetch_for_exclusive_writing_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-004");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Diana"),
            new OrderItemAdded("Contraption", 42.00m));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForExclusiveWriting<OrderAggregate, OrderNumber>(orderNumber);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.OrderNum.ShouldBe(orderNumber);
        stream.Aggregate.CustomerName.ShouldBe("Diana");
        stream.Aggregate.TotalAmount.ShouldBe(42.00m);
        stream.CurrentVersion.ShouldBe(2);
    }

    [Fact]
    public async Task natural_key_is_mutable_fetch_after_change()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var originalNumber = new OrderNumber("ORD-OLD");
        var newNumber = new OrderNumber("ORD-NEW");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(originalNumber, "Eve"));
        await theSession.SaveChangesAsync();

        // Change the natural key
        theSession.Events.Append(streamId, new OrderNumberChanged(newNumber));
        await theSession.SaveChangesAsync();

        // Fetch by the NEW natural key should succeed
        var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(newNumber);
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.OrderNum.ShouldBe(newNumber);
        stream.Aggregate.CustomerName.ShouldBe("Eve");
    }

    [Fact]
    public async Task null_natural_key_value_is_silently_skipped()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();

        // OrderCreated with a null OrderNumber - the extractor will return null
        // and the natural key projection should skip creating a mapping
        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(null, "Frank"));
        await theSession.SaveChangesAsync();

        // Appending events with non-null key afterwards should work
        var orderNumber = new OrderNumber("ORD-LATE");
        theSession.Events.Append(streamId, new OrderNumberChanged(orderNumber));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.CustomerName.ShouldBe("Frank");
    }

    [Fact]
    public async Task natural_key_with_wrapped_string_type()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<InvoiceAggregate>(SnapshotLifecycle.Inline);
        });

        var invoiceCode = new InvoiceNumber("INV-001");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<InvoiceAggregate>(streamId,
            new InvoiceCreated(invoiceCode, 250.00m));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<InvoiceAggregate, InvoiceNumber>(invoiceCode);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.InvoiceCode.ShouldBe(invoiceCode);
        stream.Aggregate.Amount.ShouldBe(250.00m);
        stream.CurrentVersion.ShouldBe(1);
    }

    #endregion

    #region Guid Stream Identity + Live Lifecycle

    [Fact]
    public async Task live_fetch_for_writing_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<OrderAggregate>();
        });

        var orderNumber = new OrderNumber("ORD-LIVE-001");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Grace"),
            new OrderItemAdded("Sprocket", 7.77m));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.OrderNum.ShouldBe(orderNumber);
        stream.Aggregate.CustomerName.ShouldBe("Grace");
        stream.Aggregate.TotalAmount.ShouldBe(7.77m);
        stream.CurrentVersion.ShouldBe(2);
    }

    [Fact]
    public async Task live_fetch_latest_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<OrderAggregate>();
        });

        var orderNumber = new OrderNumber("ORD-LIVE-002");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Hank"),
            new OrderItemAdded("Cog", 3.33m),
            new OrderCompleted());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);

        aggregate.ShouldNotBeNull();
        aggregate.OrderNum.ShouldBe(orderNumber);
        aggregate.CustomerName.ShouldBe("Hank");
        aggregate.TotalAmount.ShouldBe(3.33m);
        aggregate.IsComplete.ShouldBeTrue();
    }

    #endregion

    #region String Stream Identity + Inline Lifecycle

    [Fact]
    public async Task string_identity_fetch_for_writing_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<OrderAggregateAsString>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-STR-001");
        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<OrderAggregateAsString>(streamKey,
            new OrderCreated(orderNumber, "Iris"),
            new OrderItemAdded("Lever", 12.50m));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<OrderAggregateAsString, OrderNumber>(orderNumber);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.OrderNum.ShouldBe(orderNumber);
        stream.Aggregate.CustomerName.ShouldBe("Iris");
        stream.Aggregate.TotalAmount.ShouldBe(12.50m);
        stream.CurrentVersion.ShouldBe(2);
    }

    [Fact]
    public async Task string_identity_fetch_latest_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<OrderAggregateAsString>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-STR-002");
        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<OrderAggregateAsString>(streamKey,
            new OrderCreated(orderNumber, "Jack"),
            new OrderItemAdded("Pulley", 8.00m));
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<OrderAggregateAsString, OrderNumber>(orderNumber);

        aggregate.ShouldNotBeNull();
        aggregate.OrderNum.ShouldBe(orderNumber);
        aggregate.CustomerName.ShouldBe("Jack");
        aggregate.TotalAmount.ShouldBe(8.00m);
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task fetch_for_writing_with_expected_version_by_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-VER-001");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Karen"),
            new OrderItemAdded("Bolt", 1.50m));
        await theSession.SaveChangesAsync();

        // First fetch - version should be 2
        var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        stream.CurrentVersion.ShouldBe(2);

        // Append more events
        stream.AppendOne(new OrderItemAdded("Nut", 0.75m));
        await theSession.SaveChangesAsync();

        // Second fetch - version should now be 3
        using var session2 = theStore.LightweightSession();
        var stream2 = await session2.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        stream2.CurrentVersion.ShouldBe(3);
        stream2.Aggregate.TotalAmount.ShouldBe(2.25m);
    }

    [Fact]
    public async Task concurrent_fetches_by_natural_key_with_optimistic_concurrency()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-CONC-001");
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Leo"));
        await theSession.SaveChangesAsync();

        // Session 1 fetches for writing
        await using var session1 = theStore.LightweightSession();
        var stream1 = await session1.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        stream1.AppendOne(new OrderItemAdded("Washer", 0.25m));

        // Session 2 fetches for writing (same stream, same version)
        await using var session2 = theStore.LightweightSession();
        var stream2 = await session2.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        stream2.AppendOne(new OrderItemAdded("Screw", 0.10m));

        // First save succeeds
        await session1.SaveChangesAsync();

        // Second save should throw a concurrency exception because the version has moved
        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await session2.SaveChangesAsync();
        });
    }

    #endregion
}
