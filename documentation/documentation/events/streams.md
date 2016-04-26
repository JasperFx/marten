<!--Title:Querying Event and Stream Data-->
<!--Url:streams-->


## Fetch Events for a Stream

You can retrieve the events for a single stream at any time with the `IEventStore.FetchStream()` methods shown below:

<[sample:using-fetch-stream]>

The data returned is a list of `IEvent` objects, where each is a strong typed `Event<T>` object shown below:

<[sample:IEvent]>

## Stream State

If you just need to check on the state of an event stream - what version it is and what if any aggregate type it represents - you can use the 
`IEventStore.FetchStreamState()/FetchStreamStateAsync()` methods shown below:

<[sample:fetching_stream_state]>


## Fetch a Single Event

You can fetch the information for a single event by id, including its version number within the stream, by using `IEventStore.Load()` as shown below:

<[sample:load-a-single-event]>


## Querying Directly Against Event Data

We have to urge some caution about this functionality because it requires a search against the entire `mt_events` table. To issue Linq queries against
any specific event type, use the `IEventStore.Query<T>()` method shown below:

<[sample:query-against-event-data]>

You can use any Linq operator that Marten supports to query against event data. We think that this functionality is probably more useful for diagnostics or troubleshooting
rather than something you would routinely use to support your application. We recommend that you favor event projection views over querying within the raw event table.

