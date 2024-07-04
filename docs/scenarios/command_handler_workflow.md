# CQRS Command Handler Workflow for Capturing Events

::: tip
Definitely see the
Wolverine [Aggregate Handler Workflow](https://wolverine.netlify.app/guide/durability/marten/event-sourcing.html) for a low ceremony approach to CQRS "writes" that uses
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L21-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order_for_optimized_command_handling' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L11-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order_events_for_optimized_command_handling' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L66-L97' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_for_writing_naive' title='Start of snippet'>anchor</a></sup>
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

## Inline Aggregates <Badge type="tip" text="7.22" />

::: warning
You may need to opt out of this behavior if you are modifying the aggregate document returned by `FetchForWriting`
before new events are applied to it to not arrive at incorrectly applied projection data. 
:::

`FetchForWriting()` works with all possible projection lifecycles as of Marten 7. However, there's a wrinkle with `Inline`
projections you should be aware of. It's frequently a valuable optimization to use _Lightweight_ sessions that omit
the identity map behavior. Let's say that we have a configuration like this:

snippet: sample_using_lightweight_sessions_with_inline_single_stream_projection

As an optimization, Marten is quietly turning on the identity map behavior for just a single aggregate document type
when `FetchForWriting()` is called with an `Inline` projection as shown below:

snippet: sample_usage_of_identity_map_for_inline_projections

This was done specifically to prevent unnecessary database fetches for the exact same data within common command handler
workflow operations. There can of course be a downside if you happen to be making any kind of mutations to the aggregate
document somewhere in between calling `FetchForWriting()` and `SaveChangesAsync()`, so to opt out of this behavior if 
that causes you any trouble, use this:

::: info
Marten's default behavior of using sessions with the _Identity Map_ functionality option turned on was admittedly copied
from RavenDb almost a decade ago, and the Marten team has been too afraid to change the default behavior to the more performant, _Lightweight_ sessions because of the 
very real risk of introducing difficult to diagnose regression errors. There you go folks, a 10 year old decision that this 
author still regrets, but we're probably stuck with for the foreseeable future.
:::

## Explicit Optimistic Concurrency

This time let's explicitly opt into optimistic concurrency checks by telling Marten what the expected starting
version of the stream should be in order for the command to be processed. In this usage, you're probably assuming that
the command message was based on the starting state.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L99-L133' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetch_for_writing_explicit_optimistic_concurrency' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In this case, Marten will throw a `ConcurrencyException` if the expected starting version being passed to `FetchForWriting()` has
been incremented by some other process before this command. The same expected version check will also be evaluated during the call to
`IDocumentSession.SaveChangesAsync()`.

## Exclusive Concurrency

The last flavor of concurrency is to leverage Postgresql's ability to do row level locking and wait to achieve an exclusive lock on
the event stream. This might be applicable when the result of the command is just dependent upon the initial state of the
`Order` aggregate. This usage is shown below:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L135-L169' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample_fetch_for_writing_exclusive_lock' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that the `FetchForExclusiveWriting()` command can time out if it is unable to achieve a lock in a timely manner. In this case,
Marten will throw a `StreamLockedException`. The lock will be released when either `IDocumentSession.SaveChangesAsync()` is called or
the `IDocumentSession` is disposed.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L171-L198' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_writetoaggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
