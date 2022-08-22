# Appending Events

::: tip
Marten V5.4 introduced the new `FetchForWriting()` and `IEventStream` models that streamline the workflow of capturing events against
an aggregated "write" model.
:::

With Marten, events are captured and appended to logical "streams" of events. Marten provides
methods to create a new stream with the initial events, append events to an existing stream, and
also to append events with some protection for concurrent access to single streams.

The event data is persisted to two tables:

1. `mt_events` -- stores the actual event data and some metadata that describes the event
1. `mt_streams` -- stores information about the current state of an event stream. There is a foreign key
   relationship from `mt_events` to `mt_streams`

Events can be captured by either starting a new stream or by appending events to an existing stream. In addition, Marten has some tricks up its sleeve for dealing
with concurrency issues that may result from multiple transactions trying to simultaneously append events to the same stream.

## Starting a new Stream

You can **optionally** start a new event stream against some kind of .Net type that theoretically marks the type of stream you're capturing.
Marten does not yet use this type as anything more than metadata, but our thought is that some projections would key off this information and in a future version use that aggregate type to perform versioned snapshots of the entire stream. We may also make the aggregate type optional so that you could just supply either a string to mark the "stream type" or work without a stream type.

As usual, our sample problem domain is the Lord of the Rings style "Quest." For now, you can either start a new stream and let Marten assign the Guid id for the stream:

<!-- snippet: sample_start_stream_with_guid_identifier -->
<a id='snippet-sample_start_stream_with_guid_identifier'></a>
```cs
public async Task start_stream_with_guid_stream_identifiers(IDocumentSession session)
{
    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
    var departed = new MembersDeparted { Members = new[] { "Thom" } };

    // Let Marten assign a new Stream Id, and mark the stream with an aggregate type
    // 'Quest'
    var streamId1 = session.Events.StartStream<Quest>(joined, departed).Id;

    // Or pass the aggregate type in without generics
    var streamId2 = session.Events.StartStream(typeof(Quest), joined, departed);

    // Or instead, you tell Marten what the stream id should be
    var userDefinedStreamId = Guid.NewGuid();
    session.Events.StartStream<Quest>(userDefinedStreamId, joined, departed);

    // Or pass the aggregate type in without generics
    session.Events.StartStream(typeof(Quest), userDefinedStreamId, joined, departed);

    // Or forget about the aggregate type whatsoever
    var streamId4 = session.Events.StartStream(joined, departed);

    // Or start with a known stream id and no aggregate type
    session.Events.StartStream(userDefinedStreamId, joined, departed);

    // And persist the new stream of course
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/StartStreamSamples.cs#L40-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start_stream_with_guid_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For stream identity (strings vs. Guids), see [here](/events/configuration).

Note that `StartStream` checks for an existing stream and throws `ExistingStreamIdCollisionException` if a matching stream already exists.

## Appending Events

::: tip
`AppendEvent()` will create a new stream for the stream id if it does not already exist at the time that `IDocumentSession.SaveChanges()` is called.
:::

If you have an existing stream, you can later append additional events with `IEventStore.Append()` as shown below:

<!-- snippet: sample_append-events -->
<a id='snippet-sample_append-events'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

session.Events.Append(id, joined, departed);

session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L562-L569' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Optimistic Versioned Append

::: tip
This may not be very effective as it only helps you detect changes between calling `AppendOptimistic()`
and `SaveChangesAsync()`.
:::

You can also use the new `AppendOptimistic()` method to do optimistic concurrency with the event
stream version with an automatic stream version lookup like this:

<!-- snippet: sample_append_optimistic_event -->
<a id='snippet-sample_append_optimistic_event'></a>
```cs
public async Task append_optimistic(IDocumentSession session, Guid streamId, object[] events)
{
    // This is doing data access, so it's an async method
    await session.Events.AppendOptimistic(streamId, events);

    // Assume that there is other work happening right here...

    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/StartStreamSamples.cs#L75-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append_optimistic_event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Serialized Access to the Stream

The `AppendExclusive()` method will actually reserve a database lock on the stream itself until the
`IDocumentSession` is saved or disposed. That usage is shown below:

<!-- snippet: sample_append_exclusive_events -->
<a id='snippet-sample_append_exclusive_events'></a>
```cs
public async Task append_exclusive(IDocumentSession session, Guid streamId)
{
    // You *could* pass in events here too, but doing this establishes a transaction
    // lock on the stream.
    await session.Events.AppendExclusive(streamId);

    var events = determineNewEvents(streamId);

    // The next call can just be Append()
    session.Events.Append(streamId, events);

    // This will commit the unit of work and release the
    // lock on the event stream
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/StartStreamSamples.cs#L89-L107' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append_exclusive_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This usage will in effect serialize access to a single event stream.

## Tombstone Events

It's an imperfect world, and sometimes transactions involving Marten events will fail in process. That historically caused issues with Marten's asynchronous projection support when there were "gaps"
in the event store sequence due to failed transactions. Marten V4 introduced support for "tombstone" events where Marten tries to insert placeholder rows in the events table with the
event sequence numbers that failed in a Marten transaction. This is done strictly to improve the functioning of the [async daemon](/events/projections/async-daemon) that looks for gaps in the event sequence to "know" how
far it's safe to process asynchronous projections. If you see event rows in your database of type "tombstone", it's representative of failed transactions (maybe from optimistic concurrency violations,
transient network issues, timeouts, etc.).
