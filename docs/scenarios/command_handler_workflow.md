# CQRS Command Handler Workflow for Capturing Events

::: tip
All of this functionality originated with Marten V5.4 as a way to optimize the development workflow of typical
command handlers that possibly emit events to Marten
:::

So you're using Marten's event sourcing functionality within some kind architecture (CQRS maybe?) where your business logic needs to emit events modeling
business state changes based on external inputs (commands). These commands are most likely working on a single event stream at one time. Your business logic
will probably need to evaluate the incoming command against the current state of the event stream to either decide what events should be created, or to reject
the incoming command altogether if the system is not in the proper state for the command. And by the way, you probably also need to be concerned with concurrent access to the
business data represented by a single event stream.

## FetchForWriting

To that end, Marten has the `FetchForWriting()` operation for optimized command handling with Marten.

Let's say that you are building an order fulfillment system, so we're naturally going to model our domain as an `Order` aggregate:

snippet: sample_Order_for_optimized_command_handling

And with some events like these:

snippet: sample_Order_events_for_optimized_command_handling

Let's jump right into the first sample with simple concurrency handling:

snippet: sample_fetch_for_writing_naive

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

## Explicit Optimistic Concurrency

This time let's explicitly opt into optimistic concurrency checks by telling Marten what the expected starting
version of the stream should be in order for the command to be processed. In this usage, you're probably assuming that
the command message was based on the starting state.

The ever so slightly version of the original handler is shown below:

snippet: sample_fetch_for_writing_explicit_optimistic_concurrency

In this case, Marten will throw a `ConcurrencyException` if the expected starting version being passed to `FetchForWriting()` has
been incremented by some other process before this command. The same expected version check will also be evaluated during the call to
`IDocumentSession.SaveChangesAsync()`.

## Exclusive Concurrency

The last flavor of concurrency is to leverage Postgresql's ability to do row level locking and wait to achieve an exclusive lock on
the event stream. This might be applicable when the result of the command is just dependent upon the initial state of the
`Order` aggregate. This usage is shown below:

snippet: sample_sample_fetch_for_writing_exclusive_lock

Do note that the `FetchForExclusiveWriting()` command can time out if it is unable to achieve a lock in a timely manner. In this case,
Marten will throw a `StreamLockedException`. The lock will be released when either `IDocumentSession.SaveChangesAsync()` is called or
the `IDocumentSession` is disposed.

## WriteToAggregate

Lastly, there are several overloads of a method called `IEventStore.WriteToAggregate()` that just puts some syntactic sugar
over the top of `FetchForWriting()` to simplify the entire workflow. Using that method, our handler versions above becomes:

snippet: sample_using_WriteToAggregate
