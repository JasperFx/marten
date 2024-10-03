# CQRS Command Handler Workflow for Capturing Events

::: tip
Definitely see the
Wolverine [Aggregate Handler Workflow](https://wolverinefx.net/guide/durability/marten/event-sourcing.html) for a low ceremony approach to CQRS "writes" that uses
the `FetchForWriting()` API under the covers that is introduced in this topic.
:::

So you're using Marten's event sourcing functionality within some kind architecture (CQRS maybe?) where your business logic needs to emit events modeling
business state changes based on external inputs (commands). These commands are most likely working on a single event stream at one time. Your business logic
will probably need to evaluate the incoming command against the current state of the event stream to either decide what events should be created, or to reject
the incoming command altogether if the system is not in the proper state for the command. And by the way, you probably also need to be concerned with concurrent access to the
business data represented by a single event stream.

## FetchForWriting

::: tip
As of Marten 7, this API is usable with aggregation projections that are running with an asynchronous lifecycle. This 
is key to create "zero downtime deployments" for projection changes.
:::

::: warning
`FetchForWriting()` is only possible with single stream aggregation projections, which includes the "self-aggregating"
snapshot feature. This API assumes that it's working with one stream, and directly accesses the stream table. Multi-stream
projections will not work with this feature.
:::

To that end, Marten has the `FetchForWriting()` operation for optimized command handling with Marten.

Let's say that you are building an order fulfillment system, so we're naturally going to model our domain as an `Order` aggregate:

<!-- snippet: sample_Order_for_optimized_command_handling -->
<a id='snippet-sample_order_for_optimized_command_handling'></a>
```cs
public class Item
{
    public string Name { get; set; }
    public bool Ready { get; set; }
}

public class Order
{
    // This would be the stream id
    public Guid Id { get; set; }

    // This is important, by Marten convention this would
    // be the
    public int Version { get; set; }

    public Order(OrderCreated created)
    {
        foreach (var item in created.Items)
        {
            Items[item.Name] = item;
        }
    }

    public void Apply(IEvent<OrderShipped> shipped) => Shipped = shipped.Timestamp;
    public void Apply(ItemReady ready) => Items[ready.Name].Ready = true;

    public DateTimeOffset? Shipped { get; private set; }

    public Dictionary<string, Item> Items { get; set; } = new();

    public bool IsReadyToShip()
    {
        return Shipped == null && Items.Values.All(x => x.Ready);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L23-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order_for_optimized_command_handling' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And with some events like these:

<!-- snippet: sample_Order_events_for_optimized_command_handling -->
<a id='snippet-sample_order_events_for_optimized_command_handling'></a>
```cs
public record OrderShipped;
public record OrderCreated(Item[] Items);
public record OrderReady;

public record ItemReady(string Name);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L13-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order_events_for_optimized_command_handling' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Let's jump right into the first sample with simple concurrency handling:

<!-- snippet: sample_fetch_for_writing_naive -->
<a id='snippet-sample_fetch_for_writing_naive'></a>
```cs
public async Task Handle1(MarkItemReady command, IDocumentSession session)
{
    // Fetch the current value of the Order aggregate
    var stream = await session
        .Events
        .FetchForWriting<Order>(command.OrderId);

    var order = stream.Aggregate;

    if (order.Items.TryGetValue(command.ItemName, out var item))
    {
        // Mark that the this item is ready
        stream.AppendOne(new ItemReady(command.ItemName));
    }
    else
    {
        // Some crude validation
        throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
    }

    // If the order is ready to ship, also emit an OrderReady event
    if (order.IsReadyToShip())
    {
        stream.AppendOne(new OrderReady());
    }

    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L68-L99' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_for_writing_naive' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In this usage, `FetchForWriting<Order>()` is finding the current state of the stream based on the stream id we passed in. If the `Order` aggregate
is configured as:

1. `Live`, Marten is executing the live stream aggregation on the fly by loading all the events for this stream into memory and calculating
   the full `Order` state by applying each event in memory
2. `Inline`, Marten is loading the persisted `Order` document directly from the underlying database

Regardless of how Marten is loading or deriving the state of `Order`, it's also quietly fetching the current version of that `Order` stream
at the point that the aggregate was fetched. Stepping down inside the code, we're doing some crude validation of the current state of the
`Order` and potentially rejecting the entire command. Past that we're appending a new event for `ItemReady` and conditionally appending a
second event for `OrderReady` if every item within the `Order` is ready (for shipping I guess, this isn't really a fully formed domain model here).

After appending the events via the new `IEventStream.AppendOne()` (there's also an `AppendMany()` method), we're ready to save the new events with
the standard `IDocumentSession.SaveChangesAsync()` method call. At that point, if some other process has managed to commit changes to the same
`Order` stream between our handler calling `FetchForWriting()` and `IDocumentSession.SaveChangesAsync()`, the entire command will fail with a Marten
`ConcurrencyException`.

### Inline Optimization <Badge type="tip" text="7.25" />

If you are using and `Inline` single stream projection for the aggregate being targeted by `FetchForWriting()`, you can make a performance optimization with this setting:

<!-- snippet: sample_use_identity_map_for_inline_aggregates -->
<a id='snippet-sample_use_identity_map_for_inline_aggregates'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection("some connection string");

        // Force Marten to use the identity map for only the aggregate type
        // that is the targeted "T" in FetchForWriting<T>() when using
        // an Inline projection for the "T". Saves on Marten doing an extra
        // database fetch of the same data you already fetched from FetchForWriting()
        // when Marten needs to apply the Inline projection as part of SaveChanges()
        opts.Events.UseIdentityMapForInlineAggregates = true;
    })
    // This is non-trivial performance optimization if you never
    // need identity map mechanics in your commands or query handlers
    .UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/fetching_inline_aggregates_for_writing.cs#L520-L538' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_identity_map_for_inline_aggregates' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It's pretty involved, but the key takeaway is that _if_ you are using lightweight sessions for a performance optimization
-- and you probably should even though that's not a Marten default! -- and _also_ using `FetchForWriting<T>()` with `Inline` projections, this optimizes your system to make fewer network round trips to the database and reuse the data
you already fetched when applying the `Inline` projection. This is an _opt in_ setting because it can be harmful to existing code that might be modifying the aggregate document fetched by `FetchForWriting()` outside of Marten itself.

## Explicit Optimistic Concurrency

This time let's explicitly opt into optimistic concurrency checks by telling Marten what the expected starting
version of the stream should be in order for the command to be processed. In this usage, you're probably assuming that the command message was based on the starting state.

The ever so slightly version of the original handler is shown below:

<!-- snippet: sample_fetch_for_writing_explicit_optimistic_concurrency -->
<a id='snippet-sample_fetch_for_writing_explicit_optimistic_concurrency'></a>
```cs
public async Task Handle2(MarkItemReady command, IDocumentSession session)
{
    // Fetch the current value of the Order aggregate
    var stream = await session
        .Events

        // Explicitly tell Marten the exptected, starting version of the
        // event stream
        .FetchForWriting<Order>(command.OrderId, command.Version);

    var order = stream.Aggregate;

    if (order.Items.TryGetValue(command.ItemName, out var item))
    {
        // Mark that the this item is ready
        stream.AppendOne(new ItemReady(command.ItemName));
    }
    else
    {
        // Some crude validation
        throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
    }

    // If the order is ready to ship, also emit an OrderReady event
    if (order.IsReadyToShip())
    {
        stream.AppendOne(new OrderReady());
    }

    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L101-L135' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_for_writing_explicit_optimistic_concurrency' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In this case, Marten will throw a `ConcurrencyException` if the expected starting version being passed to `FetchForWriting()` has
been incremented by some other process before this command. The same expected version check will also be evaluated during the call to
`IDocumentSession.SaveChangesAsync()`.

## Exclusive Concurrency

The last flavor of concurrency is to leverage Postgresql's ability to do row level locking and wait to achieve an exclusive lock on the event stream. This might be applicable when the result of the command is just dependent upon the initial state of the `Order` aggregate. This usage is shown below:

<!-- snippet: sample_sample_fetch_for_writing_exclusive_lock -->
<a id='snippet-sample_sample_fetch_for_writing_exclusive_lock'></a>
```cs
public async Task Handle3(MarkItemReady command, IDocumentSession session)
{
    // Fetch the current value of the Order aggregate
    var stream = await session
        .Events

        // Explicitly tell Marten the exptected, starting version of the
        // event stream
        .FetchForExclusiveWriting<Order>(command.OrderId);

    var order = stream.Aggregate;

    if (order.Items.TryGetValue(command.ItemName, out var item))
    {
        // Mark that the this item is ready
        stream.AppendOne(new ItemReady(command.ItemName));
    }
    else
    {
        // Some crude validation
        throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
    }

    // If the order is ready to ship, also emit an OrderReady event
    if (order.IsReadyToShip())
    {
        stream.AppendOne(new OrderReady());
    }

    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L137-L171' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample_fetch_for_writing_exclusive_lock' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that the `FetchForExclusiveWriting()` command can time out if it is unable to achieve a lock in a timely manner. In this case, Marten will throw a `StreamLockedException`. The lock will be released when either `IDocumentSession.SaveChangesAsync()` is called or the `IDocumentSession` is disposed.

## WriteToAggregate

Lastly, there are several overloads of a method called `IEventStore.WriteToAggregate()` that just puts some syntactic sugar
over the top of `FetchForWriting()` to simplify the entire workflow. Using that method, our handler versions above becomes:

<!-- snippet: sample_using_WriteToAggregate -->
<a id='snippet-sample_using_writetoaggregate'></a>
```cs
public Task Handle4(MarkItemReady command, IDocumentSession session)
{
    return session.Events.WriteToAggregate<Order>(command.OrderId, command.Version, stream =>
    {
        var order = stream.Aggregate;

        if (order.Items.TryGetValue(command.ItemName, out var item))
        {
            // Mark that the this item is ready
            stream.AppendOne(new ItemReady(command.ItemName));
        }
        else
        {
            // Some crude validation
            throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
        }

        // If the order is ready to ship, also emit an OrderReady event
        if (order.IsReadyToShip())
        {
            stream.AppendOne(new OrderReady());
        }
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L173-L200' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_writetoaggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Optimizing FetchForWriting with Inline Aggregates

If you are utilizing `FetchForWriting()` for your command handlers -- and you really, really should! -- and at least some of your aggregates are updated `Inline` as shown below:

<!-- snippet: sample_registering_Order_as_Inline -->
<a id='snippet-sample_registering_order_as_inline'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // The Order aggregate is updated Inline inside the
    // same transaction as the events being appended
    opts.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);

    // Opt into an optimization for the inline aggregates
    // used with FetchForWriting()
    opts.Projections.UseIdentityMapForInlineAggregates = true;
})

// This is also a performance optimization in Marten to disable the
// identity map tracking overall in Marten sessions if you don't
// need that tracking at runtime
.UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L207-L228' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_order_as_inline' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can potentially gain some significant performance optimization by using the `UseIdentityMapForInlineAggregates` flag shown above. To be clear, this optimization mostly helps when you have the combination in a command handler that:

1. Uses `FetchForWriting` for an aggregate type
2. That aggregate type is updated or built through an `Inline` projection or snapshot

With this optimization, Marten will take steps to make sure that it uses the version of the aggregate document that was originally fetched by `FetchForWriting()` as the starting point for updating that aggregate in its `Inline` projection with the events that were appended by the command itself.

**This optimization will be harmful if you alter the loaded aggregate in any way between `FetchForWriting()` and `SaveChangesAsync()` by potentially making your projected data being saved be invalid.**
