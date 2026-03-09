# Natural Keys

Natural keys let you look up an event stream by a domain-meaningful identifier (like an order number or invoice code) instead of by its internal stream id. Marten maintains a separate lookup table that maps natural key values to stream ids, so you can use `FetchForWriting` and `FetchLatest` with your natural key in a single database round-trip.

## When to Use Natural Keys

Use natural keys when:

- External systems or users reference aggregates by a business identifier (e.g., `"ORD-12345"`) rather than a `Guid` stream id
- You need to look up streams by a human-readable identifier without maintaining your own separate index
- Your aggregate has a stable "business key" that may occasionally change (natural keys support mutation)

## Declaring Natural Keys

Mark a property on your aggregate with `[NaturalKey]`, and mark the methods that set or change the key value with `[NaturalKeySource]`:

<!-- snippet: sample_natural_key_aggregate_types -->
<a id='snippet-sample_natural_key_aggregate_types'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/fetching_by_natural_key.cs#L16-L109' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_natural_key_aggregate_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `[NaturalKeySource]` attribute tells Marten which `Create` / `Apply` methods produce or change the natural key value. Marten uses this information to keep the lookup table in sync whenever events are appended.

## Event-to-Key Mappings

Every event type that sets or changes the natural key must be declared through the `[NaturalKeySource]` attribute. When Marten processes events during an append operation, it extracts the key value from these mapped events and writes it to the lookup table.

Events that do not affect the natural key (like `OrderItemAdded` in the example above) do not need any mapping.

## Storage

Marten automatically creates and manages a lookup table for each aggregate type that has a natural key configured. The table maps natural key values to stream ids and is:

- Created automatically during schema migrations
- Partition-aware when using tenanted streams
- Updated transactionally alongside event appends
- Archive-aware (archived streams are excluded from lookups)

You do not need to create or manage this table yourself.

## FetchForWriting by Natural Key

The primary use case for natural keys is looking up a stream for writing without knowing its stream id:

<!-- snippet: sample_marten_fetch_for_writing_by_natural_key -->
<a id='snippet-sample_marten_fetch_for_writing_by_natural_key'></a>
```cs
// FetchForWriting by the business identifier instead of stream id
var stream = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);

stream.Aggregate.ShouldNotBeNull();
stream.Aggregate.OrderNum.ShouldBe(orderNumber);

// Append new events through the stream
stream.AppendOne(new OrderItemAdded("Gadget", 19.99m));
await theSession.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/fetching_by_natural_key.cs#L147-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_fetch_for_writing_by_natural_key' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This resolves the natural key to a stream id and fetches the aggregate in a single database round-trip.

## FetchLatest by Natural Key

For read-only access, you can use `FetchLatest` with a natural key:

<!-- snippet: sample_marten_fetch_latest_by_natural_key -->
<a id='snippet-sample_marten_fetch_latest_by_natural_key'></a>
```cs
// Read-only access by natural key
var aggregate = await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/fetching_by_natural_key.cs#L209-L212' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_fetch_latest_by_natural_key' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Mutability

Natural keys can change over the lifetime of a stream. When an event mapped with `[NaturalKeySource]` is appended, Marten updates the lookup table with the new value. The old key value is replaced, so lookups using the previous key will no longer resolve to that stream.

## Null and Default Keys

If a mapped event produces a `null` or default key value, Marten silently skips writing to the lookup table. This means streams where the natural key has not yet been assigned will not appear in natural key lookups, but will still be accessible by stream id.

## Clean and Maintenance Operations

The natural key lookup table is maintained automatically as part of normal event appending. If you need to rebuild the lookup table (for example, after a data migration), you can do so through Marten's schema management tools as part of a projection rebuild.

## Testing Considerations

When writing integration tests:

- Natural key lookups work against the same session's uncommitted data, so you can append events and look up by natural key within the same unit of work
- If you are using `FetchForWriting` with a natural key that does not exist, the behavior is the same as with a stream id that does not exist

## Integration with Wolverine

Natural keys integrate with Wolverine's aggregate handler workflow. See the [Wolverine documentation on natural keys with Marten](https://wolverinefx.net/guide/durability/marten/event-sourcing.html#natural-keys) for details on how Wolverine resolves natural keys from command properties.
