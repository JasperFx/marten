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

Every owned key needs at least one event that sets it, through `[NaturalKeySource]` or `NaturalKeyFor()`.

An aggregate can own at most one natural key. It can also borrow keys owned by other aggregates of
the same stream, see [Mapped Natural Keys](#mapped-natural-keys). Every key on one aggregate needs
a distinct .NET type, because the type is what `FetchForWriting<T, TKey>` and `FetchLatest<T, TKey>`
resolve the key by.

### Nullable Keys <Badge type="tip" text="10.0" />

::: warning
Before 10.0 nullable keys had limited support. A `[NaturalKeySource]` method that returned `Nullable<TKey>`
would not be recognized as a natural key source and silently ignored. For natural keys that were reference types,
returning a null value would be a no-op.
:::

An aggregate may not have a natural key for its entire lifetime. It may be set after stream creation or unset later on.
This is supported for both reference and struct keys. Both `TKey` and `TKey?` can be used as the aggregate property, on an event, and as the return type of a `[NaturalKeySource]` method and they will all be detected and handled.

The fetch APIs take `TKey` only. `FetchForWriting<T, TKey?>`, `FetchLatest<T, TKey?>` and the batch forms throw
an `ArgumentException`. A null key can't match a stream, and you know when you have one, so check for null and
fetch by its value. If you do pass null in, it will be treated the same as these methods would treat null for a stream key.

<!-- snippet: sample_nullable_natural_key_aggregate -->
<a id='snippet-sample_nullable_natural_key_aggregate'></a>
```cs
public readonly record struct Sku(string Value);

public record ProductListed(Sku Sku, string Name);

public record ProductSkuChanged(Sku? NewSku);

public record ProductRestocked(int Quantity);

// The key is a struct, declared nullable on the aggregate, and set by sources returning both Sku and Sku?
public class Product
{
    public Guid Id { get; set; }

    [NaturalKey]
    public Sku? Sku { get; set; }

    public string Name { get; set; } = string.Empty;
    public int Stock { get; set; }

    [NaturalKeySource]
    public static Sku KeyFor(ProductListed e) => e.Sku;

    [NaturalKeySource]
    public static Sku? KeyFor(ProductSkuChanged e) => e.NewSku;

    public void Apply(ProductListed e)
    {
        Sku = e.Sku;
        Name = e.Name;
    }

    public void Apply(ProductSkuChanged e) => Sku = e.NewSku;

    public void Apply(ProductRestocked e) => Stock += e.Quantity;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/nullable_natural_keys.cs#L13-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_nullable_natural_key_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

An aggregate whose key is `null` has no lookup row, and cannot be found by natural key lookup. See
[Null and Default Keys](#null-and-default-keys).

## Event-to-Key Mappings

Every event type that sets or changes the natural key must be declared through the `[NaturalKeySource]` attribute. When Marten processes events during an append operation, it extracts the key value from these mapped events and writes it to the lookup table.

Events that do not affect the natural key (like `OrderItemAdded` in the example above) do not need any mapping.

### Handler Signature Requirements

The lookup table is written _inline_ as events are appended, well before any projection has built the
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
depends on the _previous_ aggregate state, and do not read any aggregate state other than the natural key
inside one. Under strategy 3 Marten calls your method with a _blank_ aggregate, so everything else on it
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

A source method also has to follow these rules: <Badge type="tip" text="10.0" />

- **It has to be public.** Marten only binds public methods, static or instance, on the aggregate or on a
  separate projection class.
- **One source per event type.** Two `[NaturalKeySource]` methods for the same event are only allowed when
  both read the key from the same property of the event. That covers the usual pairing of a `Create` for the
  first event and an `Apply` for later ones. Otherwise Marten could not tell which of the two keys is meant.
- **Only an owned key has sources.** An aggregate whose keys are all [mapped](#mapped-natural-keys) is kept in
  the lookup by the projection that owns the key, so `[NaturalKeySource]` methods on it are an error.

If none of the three strategies can bind a method, or a method breaks one of these rules, Marten throws an
`InvalidProjectionException` when the projection is registered, naming the method and the reason. (Before
Marten 9.20 / JasperFx.Events 2.36.0 an unbindable method was silently dropped, so the lookup table was
simply never written for that event type and the first sign of trouble was a natural key lookup returning
null at runtime — see [JasperFx/jasperfx#569](https://github.com/JasperFx/jasperfx/issues/569).)

### Explicit Registration <Badge type="tip" text="9.20" />

When attribute discovery cannot bind your method — or when you would simply rather be explicit — register
the mapping directly with `NaturalKeyFor()` on the projection. An explicit registration replaces whatever
discovery found for the same event type and clears the configuration-time error an unbindable method
would otherwise raise. The aggregate then only declares the key:

<!-- snippet: sample_natural_key_registered_order -->
<a id='snippet-sample_natural_key_registered_order'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_documentation_samples.cs#L14-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_natural_key_registered_order' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

and the store registers the event mappings for it:

<!-- snippet: sample_natural_key_explicit_registration -->
<a id='snippet-sample_natural_key_explicit_registration'></a>
```cs
opts.Projections.Snapshot<RegisteredOrder>(SnapshotLifecycle.Inline, p =>
    ((SingleStreamProjection<RegisteredOrder, Guid>)p).NaturalKeyFor(x => x
        // Read the key from the event body
        .SetBy<OrderCreated>(e => e.OrderNumber)
        // ...or from the whole event, when the key depends on its metadata, such as the
        // stream key, timestamp or headers
        .SetByEvent<OrderNumberChanged>(e => e.Data.NewOrderNumber)));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_documentation_samples.cs#L130-L140' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_natural_key_explicit_registration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The same `NaturalKeyFor()` method is available on a projection class you register with
`Projections.Add(...)`. Call it from the projection's constructor:

<!-- snippet: sample_natural_key_explicit_registration_in_projection_class -->
<a id='snippet-sample_natural_key_explicit_registration_in_projection_class'></a>
```cs
public class RegisteredOrderProjection: SingleStreamProjection<RegisteredOrder, Guid>
{
    public RegisteredOrderProjection()
    {
        NaturalKeyFor(x => x
            .SetBy<OrderCreated>(e => e.OrderNumber)
            .SetBy<OrderNumberChanged>(e => e.NewOrderNumber));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_documentation_samples.cs#L34-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_natural_key_explicit_registration_in_projection_class' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Storage

Marten automatically creates and manages a lookup table for each aggregate type that has a natural key configured. The table maps natural key values to stream ids and is:

- Created automatically during schema migrations
- Partition-aware when using tenanted streams
- Updated transactionally alongside event appends
- Archive-aware (archived streams are excluded from lookups)

You do not need to create or manage this table yourself.

## Uniqueness

It is assumed that there is a one-to-one mapping between natural key values and stream ids in an aggregate. If a second
stream attempts to set a natural key that is already used, `SaveChangesAsync` throws a `DuplicateNaturalKeyException`, and nothing in the session is saved:

<!-- snippet: sample_natural_key_duplicate_claim -->
<a id='snippet-sample_natural_key_duplicate_claim'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_documentation_samples.cs#L73-L89' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_natural_key_duplicate_claim' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The exception names the key, the aggregate type, the stream that holds the key and the stream that tried to
claim it. A key that a stream has [changed](#mutability) or [retired](#null-and-default-keys) is available
for another stream to use, and so is the key of an archived stream.

Wolverine's `MapConcurrencyFailuresToConflict()` answers a `DuplicateNaturalKeyException` with a 409, as it does
for a concurrency failure.

## Fetching by Natural Key

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

### Expected Version

As with a stream id, you can pass the version you expect the stream to be at. The fetch fails immediately
with a `ConcurrencyException` if the stream has moved on:

<!-- snippet: sample_fetch_for_writing_by_natural_key_with_expected_version -->
<a id='snippet-sample_fetch_for_writing_by_natural_key_with_expected_version'></a>
```cs
var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(order, 3);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_expected_version.cs#L45-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_for_writing_by_natural_key_with_expected_version' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A natural key that doesn't match any stream is at version 0.

::: warning
Be careful using expected version with natural keys if you intend to change the natural key for an aggregate
over time. Expected version is checked against the version of the stream currently mapped to the natural key.
Assigning the natural key to a different stream may cause the expected version check to pass even when it
should fail, due to the different history in the new stream. Therefore it is recommended to not change natural
keys over time if you intend to query with expected version.
:::

### Exclusive Writes

`FetchForExclusiveWriting<T, TKey>` resolves the key and takes the same exclusive lock on the stream as a fetch
by stream id. The lock is held until the session saves its changes or is disposed:

<!-- snippet: sample_fetch_for_exclusive_writing_by_natural_key -->
<a id='snippet-sample_fetch_for_exclusive_writing_by_natural_key'></a>
```cs
// Locks the stream until the session is saved or disposed
var stream = await session.Events.FetchForExclusiveWriting<OrderAggregate, OrderNumber>(orderNumber);

stream.AppendOne(new OrderItemAdded("Gadget", 19.99m));
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_documentation_samples.cs#L108-L116' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_for_exclusive_writing_by_natural_key' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A natural key that doesn't match any stream starts a new stream, as `FetchForWriting` does.

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

### Batch Queries

`FetchForWriting<T, TKey>`, `FetchForExclusiveWriting<T, TKey>` and `FetchLatest<T, TKey>` are also available
on a batch query.

<!-- snippet: sample_batch_fetch_by_natural_key -->
<a id='snippet-sample_batch_fetch_by_natural_key'></a>
```cs
var batch = session.CreateBatchQuery();

var summary = batch.Events.FetchLatest<OrderSummary, OrderNumber>(order);
var owner = batch.Events.FetchLatest<OrderAggregate, OrderNumber>(order);
var byStreamId = batch.Events.FetchLatest<OrderAggregate>(orderId);
var invoiceAggregate = batch.Events.FetchLatest<InvoiceAggregate, InvoiceNumber>(invoice);
var missing = batch.Events.FetchLatest<InvoiceAggregate, InvoiceNumber>(new InvoiceNumber("INV-NONE"));

// One round trip for all five
await batch.Execute();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/batch_querying_by_natural_key.cs#L51-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch_fetch_by_natural_key' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### FetchManyForWriting by Natural Key

`FetchManyForWriting<T, TKey>` fetches several streams for writing in one round trip by natural key:

<!-- snippet: sample_fetch_many_for_writing_by_natural_key -->
<a id='snippet-sample_fetch_many_for_writing_by_natural_key'></a>
```cs
var streams = await session.Events.FetchManyForWriting<OrderAggregate, OrderNumber>([first, second]);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_fetch_many_for_writing.cs#L47-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_many_for_writing_by_natural_key' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The keys are `TKey`, never `Nullable<TKey>` (i.e. `TKey?`), and none may be null. Drop the keys you know to be missing first.

## Natural Keys on Async Projections

The lookup table is written inline as events are appended, whatever the lifecycle of the aggregate that owns
the key. An `Async` aggregate can therefore own a natural key like any other. `FetchLatest` and
`FetchForWriting` by natural key return its current state: they fold the events the daemon has not projected
yet onto the stored snapshot, exactly as a fetch by stream id does.

This does however mean that you are losing one of the key benefits of using async projections; the natural key
lookup table is adding to the overhead of writing events even when the rest of the async projection is not. To
avoid this, consider using [mapped natural keys](#mapped-natural-keys).

## Mapped Natural Keys

When there are many aggregates for the same stream, it is likely that they can all use the same natural key.
Rather than defining it for every aggregate, you can define it once for the stream and _map_ it to each aggregate,
with `[MappedNaturalKey<TSource>]` or `[NaturalKey(SourceProjection = typeof(TSource))]`. `TSource` can be either
the aggregate that defines the natural key or its projection class:

<!-- snippet: sample_mapped_natural_key_aggregate -->
<a id='snippet-sample_mapped_natural_key_aggregate'></a>
```cs
// An async read model of an order stream. It owns no lookup: the order number resolves through
// OrderAggregate's mt_natural_key table, which OrderAggregate's projection maintains inline.
public class OrderSummary
{
    public Guid Id { get; set; }

    [MappedNaturalKey<OrderAggregate>]
    public OrderNumber Number { get; set; }

    public int Items { get; set; }

    public static OrderSummary Create(OrderCreated e) => new() { Number = e.OrderNumber };

    public void Apply(OrderItemAdded e) => Items++;

    public void Apply(OrderNumberChanged e) => Number = e.NewOrderNumber;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/mapped_and_async_natural_keys.cs#L19-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_mapped_natural_key_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A mapped key has no lookup table of its own. It reads the table of the aggregate that owns the key, so
register the owner too:

<!-- snippet: sample_registering_mapped_natural_key_projections -->
<a id='snippet-sample_registering_mapped_natural_key_projections'></a>
```cs
// The owner maintains the order number lookup inline...
opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);

// ...and the async view is fetched through it
opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/mapped_and_async_natural_keys.cs#L96-L102' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_mapped_natural_key_projections' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Mapped keys suit an `Async` read model well. The owner keeps the lookup current inline, so the read model can
be fetched by the key straight away, before the daemon has caught up. No additional lookup table for the async
read model is needed:

<!-- snippet: sample_fetch_async_aggregate_by_mapped_natural_key -->
<a id='snippet-sample_fetch_async_aggregate_by_mapped_natural_key'></a>
```cs
// Current state, whether or not the daemon has caught up yet
var latest = await theSession.Events.FetchLatest<OrderSummary, OrderNumber>(orderNumber);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/mapped_and_async_natural_keys.cs#L113-L116' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_async_aggregate_by_mapped_natural_key' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

An aggregate can map any number of keys, alongside at most one key it owns, as long as every key has a
distinct type. When the `DocumentStore` is built, Marten checks that each mapped key's source:

- is registered as a projection;
- owns a natural key (a mapped key cannot read another mapped key);
- owns a key of the same type, where `TKey` and `TKey?` count as the same;
- is not the aggregate itself;
- is a global aggregate when the mapping aggregate is also one.

Any issues will result in Marten raising an `InvalidProjectionException` with details about what went wrong.

### Composite Projections

An aggregate in a [composite projection](/events/projections/composite) can't own a natural key. Building the
`DocumentStore` throws an `InvalidProjectionException` if one does. A member of a composite projection can still
be fetched by a mapped key, as long as the key's owner is registered outside the composite:

<!-- snippet: sample_mapped_natural_key_in_a_composite_projection -->
<a id='snippet-sample_mapped_natural_key_in_a_composite_projection'></a>
```cs
// The owner of the order number key is registered on its own...
opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);

// ...so a member of a composite projection can be fetched by that key through a mapped key
opts.Projections.CompositeProjectionFor("Summaries", c =>
{
    c.Snapshot<OrderSummary>();
    c.Snapshot<SimpleAggregate>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/FetchForWriting/natural_key_store_shapes.cs#L60-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_mapped_natural_key_in_a_composite_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Mutability

Natural keys can change over the lifetime of a stream. When an event mapped with `[NaturalKeySource]` is appended, Marten updates the lookup table with the new value. The old key value is replaced, so lookups using the previous key will no longer resolve to that stream.

The retired row is removed rather than left behind, so the previous value immediately becomes available for another stream to claim.

## Null and Default Keys

When a `[NaturalKeySource]` produces a `null` or default key value, the stream has no natural key. If the stream
has never been assigned a key, it has no lookup row. If it used to have a key and it is now set to `null` then
the lookup row is retired - the stream can no longer be found by the old key, and that key is free for another
stream to claim.

## Clean and Maintenance Operations

The natural key lookup table is maintained automatically as part of normal event appending. If you need to rebuild the lookup table (for example, after a data migration), you can do so through Marten's schema management tools as part of a projection rebuild.

## Testing Considerations

When writing integration tests:

- The lookup row for a key is written when the session's changes are saved, so a stream started in the current session can't be found by its natural key until then
- If you are using `FetchForWriting` with a natural key that does not exist, the behavior is the same as with a stream id that does not exist
- A key belongs to one stream at a time, so a test that starts a stream with a fixed key fails with a `DuplicateNaturalKeyException` when it runs a second time against the same database. Clear the event data first, or use a fresh key in each test

## Build-Time Diagnostics

The JasperFx source generator reports natural key mistakes as compiler errors, ahead of the runtime checks:

| Id          | Rule                                                                                                                                                                                    |
| ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `JFXEVT010` | An aggregate owns more than one natural key                                                                                                                                             |
| `JFXEVT011` | Two natural keys on one aggregate share a type (`TKey` and `TKey?` count as the same)                                                                                                   |
| `JFXEVT012` | A mapped key names its own aggregate or projection as its source                                                                                                                        |
| `JFXEVT013` | A mapped key's source is not an aggregate projection, or owns no natural key                                                                                                            |
| `JFXEVT014` | A mapped key's source key is itself mapped                                                                                                                                              |
| `JFXEVT015` | A mapped key's type does not match the type of the key its source owns                                                                                                                  |
| `JFXEVT016` | Wolverine's `[WriteAggregate<TKey>]`, `[ReadAggregate<TKey>]`, `[WriteModel<TKey>]` or `[ReadModel<TKey>]` (or `NaturalKey = typeof(TKey)`) names a key type the aggregate doesn't have |
| `JFXEVT017` | One of those attributes names `TKey?` instead of `TKey`                                                                                                                                 |

## Integration with Wolverine

Natural keys integrate with Wolverine's aggregate handler workflow. See the [Wolverine documentation on natural keys with Marten](https://wolverinefx.net/guide/durability/marten/event-sourcing.html#natural-keys) for details on how Wolverine resolves natural keys from command properties.
