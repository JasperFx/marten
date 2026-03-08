# Natural Keys

Natural keys let you look up an event stream by a domain-meaningful identifier (like an order number or invoice code) instead of by its internal stream id. Marten maintains a separate lookup table that maps natural key values to stream ids, so you can use `FetchForWriting` and `FetchLatest` with your natural key in a single database round-trip.

## When to Use Natural Keys

Use natural keys when:

- External systems or users reference aggregates by a business identifier (e.g., `"ORD-12345"`) rather than a `Guid` stream id
- You need to look up streams by a human-readable identifier without maintaining your own separate index
- Your aggregate has a stable "business key" that may occasionally change (natural keys support mutation)

## Declaring Natural Keys

### Using Attributes

Mark a property on your aggregate with `[NaturalKey]`, and mark the methods that set or change the key value with `[NaturalKeySource]`:

```cs
// Strong-typed identifier wrapping a string
public record OrderId(string Value);

// Events
public record OrderCreated(OrderId OrderId, string CustomerName);
public record OrderNumberChanged(OrderId NewOrderId);
public record OrderItemAdded(string ProductName, int Quantity);

// Aggregate with natural key
public class Order
{
    public Guid Id { get; set; }  // Stream id (surrogate key)

    [NaturalKey]
    public OrderId OrderNumber { get; set; }  // Natural key

    public string CustomerName { get; set; }
    public List<string> Items { get; set; } = new();

    [NaturalKeySource]
    public static Order Create(OrderCreated e) => new()
    {
        OrderNumber = e.OrderId,
        CustomerName = e.CustomerName
    };

    [NaturalKeySource]
    public void Apply(OrderNumberChanged e) => OrderNumber = e.NewOrderId;

    public void Apply(OrderItemAdded e) => Items.Add(e.ProductName);
}
```

The `[NaturalKeySource]` attribute tells Marten which `Create` / `Apply` methods produce or change the natural key value. Marten uses this information to keep the lookup table in sync whenever events are appended.

### Using the Fluent API

If you prefer to keep your aggregate free of Marten-specific attributes, you can configure natural keys in a projection class:

```cs
public class OrderProjection : SingleStreamProjection<Order, Guid>
{
    public OrderProjection()
    {
        NaturalKey(x => x.OrderNumber)
            .SetBy<OrderCreated>(e => e.OrderId)
            .SetBy<OrderNumberChanged>(e => e.NewOrderId);
    }

    public static Order Create(OrderCreated e) => new()
    {
        OrderNumber = e.OrderId,
        CustomerName = e.CustomerName
    };

    public void Apply(OrderNumberChanged e, Order order) => order.OrderNumber = e.NewOrderId;
    public void Apply(OrderItemAdded e, Order order) => order.Items.Add(e.ProductName);
}
```

The `NaturalKey()` method identifies the property, and `SetBy<TEvent>()` tells Marten which events carry a new value for the key.

## Event-to-Key Mappings

Every event type that sets or changes the natural key must be declared either through the `[NaturalKeySource]` attribute or the fluent `SetBy<TEvent>()` call. When Marten processes events during an append operation, it extracts the key value from these mapped events and writes it to the lookup table.

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

```cs
// Standard: FetchForWriting by stream id
var stream = await session.Events.FetchForWriting<Order>(streamId);

// With natural key: FetchForWriting by the business identifier
var stream = await session.Events.FetchForWriting<Order, OrderId>(new OrderId("ORD-12345"));
stream.AppendOne(new OrderItemAdded("Widget", 3));
await session.SaveChangesAsync();
```

This resolves the natural key to a stream id and fetches the aggregate in a single database round-trip.

## FetchLatest by Natural Key

For read-only access, you can use `FetchLatest` with a natural key:

```cs
var order = await session.Events.FetchLatest<Order, OrderId>(new OrderId("ORD-12345"));
```

## Mutability

Natural keys can change over the lifetime of a stream. When an event mapped with `[NaturalKeySource]` or `SetBy<TEvent>()` is appended, Marten updates the lookup table with the new value. The old key value is replaced, so lookups using the previous key will no longer resolve to that stream.

## Null and Default Keys

If a mapped event produces a `null` or default key value, Marten silently skips writing to the lookup table. This means streams where the natural key has not yet been assigned will not appear in natural key lookups, but will still be accessible by stream id.

## Clean and Maintenance Operations

The natural key lookup table is maintained automatically as part of normal event appending. If you need to rebuild the lookup table (for example, after a data migration), you can do so through Marten's schema management tools as part of a projection rebuild.

## Testing Considerations

When writing integration tests:

- Natural key lookups work against the same session's uncommitted data, so you can append events and look up by natural key within the same unit of work
- If you are using `FetchForWriting` with a natural key that does not exist, the behavior is the same as with a stream id that does not exist

## Integration with Wolverine

Natural keys integrate with Wolverine's aggregate handler workflow. See the [Wolverine documentation on natural keys with Marten](/guide/durability/marten/event-sourcing#natural-keys) for details on how Wolverine resolves natural keys from command properties.
