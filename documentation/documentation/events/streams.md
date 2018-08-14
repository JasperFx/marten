<!--Title:Querying Event and Stream Data-->
<!--Url:streams-->

## Fetch Events for a Stream

You can retrieve the events for a single stream at any time with the `IEventStore.FetchStream()` methods shown below:

<[sample:using-fetch-stream]>

The data returned is a list of `IEvent` objects, where each is a strongly-typed `Event<T>` object shown below:

<[sample:IEvent]>

## Stream State

If you just need to check on the state of an event stream - what version (effectively the number of events in the stream) it is and what, if any, aggregate type it represents - you can use the `IEventStore.FetchStreamState()/FetchStreamStateAsync()` methods or `IBatchQuery.Events.FetchStreamState()`, as shown below:

<[sample:fetching_stream_state]>

Furthermore, `StreamState` contains metadata for when the stream was created, `StreamState.Created`, and when the stream was last updated, `StreamState.LastTimestamp`.

## Fetch a Single Event

You can fetch the information for a single event by id, including its version number within the stream, by using `IEventStore.Load()` as shown below:

<[sample:load-a-single-event]>

## Querying Directly Against Event Data

We urge caution about this functionality because it requires a search against the entire `mt_events` table. To issue Linq queries against any specific event type, use the method shown below:

<[sample:query-against-event-data]>

You can use any Linq operator that Marten supports to query against event data. We think that this functionality is probably more useful for diagnostics or troubleshooting rather than something you would routinely use to support your application. We recommend that you favor event projection views over querying within the raw event table.

With Marten 1.0, you can issue queries with Marten's full Linq support against the raw event data with this method:

<[sample:example_of_querying_for_event_data]>

This mechanism will allow you to query by any property of the `IEvent` interface shown above.