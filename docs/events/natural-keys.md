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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/fetching_by_natural_key.cs#L18-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_natural_key_aggregate_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `[NaturalKeySource]` attribute tells Marten which `Create` / `Apply` methods produce or change the natural key value. Marten uses this information to keep the lookup table in sync whenever events are appended.

## Event-to-Key Mappings

Every event type that sets or changes the natural key must be declared through the `[NaturalKeySource]` attribute. When Marten processes events during an append operation, it extracts the key value from these mapped events and writes it to the lookup table.

Events that do not affect the natural key (like `OrderItemAdded` in the example above) do not need any mapping.

### Handler Signature Requirements

The lookup table is written *inline* as events are appended, well before any projection has built the
aggregate — that is what lets `FetchForWriting` by natural key work even when the snapshot lifecycle is
`Async`. The key value therefore has to be derivable from the event alone.

Marten tries three strategies for a `[NaturalKeySource]` method, in descending order of trustworthiness:

1. **A static method returning the natural key type**, taking either the raw event or `IEvent<TEvent>` —
   for example `public static OrderNumber KeyFor(IEvent<OrderRenumbered> e) => new(e.Data.NewNumber)`.
   This is a pure function of the event: nothing is fabricated and none of your aggregation code runs to
   work the key out. Prefer it.
2. **A property of the key's type carried on the event body**, when there is exactly one. An event that
   carries both the old and the new key is ambiguous, so this strategy declines rather than guessing.
3. **Invoking your method against a blank aggregate** — a static factory or evolve method such as
   `public static Order Create(OrderCreated e)`, or an instance `Apply(TEvent)` whose body sets the key.
   The key is read off whatever the method returned.

::: warning
**A `[NaturalKeySource]` method never sees the current aggregate.** Do not write one whose new key value
depends on the *previous* aggregate state, and do not read any aggregate state other than the natural key
inside one. Under strategy 3 Marten calls your method with a *blank* aggregate, so everything else on it
is `null` or default.

This is a consequence of when the lookup table is written, not an oversight. The table is maintained
inline at append time — that is the whole reason a natural key lookup works under an `Async` snapshot
lifecycle, where the aggregate may not have been built yet, and it is why the key has to be a function of
the event alone. A method that needs the prior state to work out the new key cannot be supported here;
carry the value you need on the event instead.

Strategy 3 is skipped entirely when the aggregate cannot be safely constructed — most commonly because it
declares `required` members that a parameterless constructor cannot satisfy. Marten will not hand your
method an instance that C# itself would not have let you create. Use strategy 1 or the explicit
registration below for those types.
:::

If none of the three can bind a method, Marten throws an `InvalidProjectionException` when the projection
is registered, naming the method and the reason. (Before Marten 9.20 / JasperFx.Events 2.36.0 an
unbindable method was silently dropped, so the lookup table was simply never written for that event type
and the first sign of trouble was a natural key lookup returning null at runtime — see
[JasperFx/jasperfx#569](https://github.com/JasperFx/jasperfx/issues/569).)

### Explicit Registration <Badge type="tip" text="9.20" />

When attribute discovery cannot bind your method — or when you would simply rather be explicit — register
the mapping directly with `NaturalKeyFor()` on the projection. An explicit registration replaces whatever
discovery found for the same event type and clears the configuration-time error an unbindable method
would otherwise raise:

```cs
opts.Projections.Snapshot<Order>(SnapshotLifecycle.Async, p =>
    ((SingleStreamProjection<Order, Guid>)p).NaturalKeyFor(x => x
        // Derive the key from the event body
        .SetBy<OrderCreated>(e => new OrderNumber(e.Number))
        // ...or from the whole event, when the key depends on metadata
        // such as the stream key, timestamp, or headers
        .SetByEvent<OrderRenumbered>(e => new OrderNumber(e.Data.NewNumber))));
```

The same `NaturalKeyFor()` method is available on a projection class you register with
`Projections.Add(...)`; call it from the projection's constructor.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/fetching_by_natural_key.cs#L149-L159' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_fetch_for_writing_by_natural_key' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/fetching_by_natural_key.cs#L211-L214' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_fetch_latest_by_natural_key' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Mutability

Natural keys can change over the lifetime of a stream. When an event mapped with `[NaturalKeySource]` is appended, Marten updates the lookup table with the new value. The old key value is replaced, so lookups using the previous key will no longer resolve to that stream.

The retired row is removed rather than left behind, so the previous value immediately becomes available for another stream to claim.

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
