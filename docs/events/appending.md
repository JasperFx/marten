# Appending Events

::: tip
For CQRS style command handlers that append events to an existing event stream, the Marten team very
strongly recommends the [FetchForWriting](/scenarios/command_handler_workflow) API. This API is used underneath
the Wolverine [Aggregate Handler Workflow](https://wolverinefx.net/guide/durability/marten/event-sourcing.html) that is probably the very simplest possible way to build command handlers
with Marten event sourcing today.
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

## "Rich" vs "Quick" Appends <Badge type="tip" text="7.25" />

::: tip
Long story short, the new "Quick" model appears to provide much better performance and scalability.
:::

Before diving into starting new event streams or appending events to existing streams, just know that there are two different
modes of event appending you can use with Marten:

<!-- snippet: sample_configuring_event_append_mode -->
<a id='snippet-sample_configuring_event_append_mode'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        // This is the default Marten behavior from 4.0 on
        opts.Events.AppendMode = EventAppendMode.Rich;

        // Lighter weight mode that should result in better
        // performance, but with a loss of available metadata
        // within inline projections
        opts.Events.AppendMode = EventAppendMode.Quick;
    })
    .UseNpgsqlDataSource();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/QuickAppend/Examples.cs#L12-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring_event_append_mode' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The classic `Rich` mode will append events in a two step process where the local session will first determine all possible
metadata for the events about to be appended such that inline projections can use event versions and the global event sequence
numbers at the time that the inline projections are created. 

::: warning
If you are using `Inline` projections with the "Quick" mode, just be aware that you will not have access to the final
event sequence or stream version at the time the projections are built. Marten _is_ able to set the stream version into
a single stream projection document built `Inline`, but that's done on the server side. Just be warned.
:::

The newer `Quick` mode eschews version and sequence metadata in favor of performing the event append and stream creation
operations with minimal overhead. The improved performance comes at the cost of not having the `IEvent.Version` and `IEvent.Sequence`
information available at the time that inline projections are executed.

From initial load testing, the "Quick" mode appears to lead to a 40-50% time reduction Marten's process of appending
events. Your results will vary of course. Maybe more importantly, the "Quick" mode seems to make a large positive
in the functioning of the asynchronous projections and subscriptions by preventing the event "skipping" issue that
can happen with the "Rich" mode when a system becomes slow under heavy loads. Lastly, the Marten team believes that the
"Quick" mode can alleviate concurrency issues from trying to append events to the same stream without utilizing optimistic
or exclusive locking on the stream.

If using inline projections for a single stream (`SingleStreamProjection` or _snapshots_) and the `Quick` mode, the Marten team
highly recommends using the `IRevisioned` interface on your projected aggregate documents so that Marten can "move" the version
set by the database operations to the version of the projected documents loaded from the database later. Mapping a custom member
to the `Revision` metadata will work as well.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L582-L591' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Mandatory Stream Types <Badge type="tip" text="7.30" />

::: warning
Absolutely use this flag on new development work or when you want to take advantage of the optimized projection rebuilds
introduced in Marten 7.30, but be aware of the consequences outlined in this section. 
:::

The default behavior in Marten is to allow you to happily start event streams without a stream type marker (the "T" in `StartStream<T>()`),
but in some cases there are optimizations that Marten can do for performance if it can assume the stream type marker
is present in the database:

* The optimized single stream projection rebuilds
* Specifying event filtering on a projection running asynchronously where Marten cannot derive the event types itself --
  like you'd frequently encounter with projections using explicit code instead of the aggregation method conventions

To make the stream type markers mandatory, you can use this flag in the configuration:

<!-- snippet: sample_UseMandatoryStreamTypeDeclaration -->
<a id='snippet-sample_usemandatorystreamtypedeclaration'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // Force users to supply a stream type on StartStream, and disallow
    // appending events if the stream does not already exist
    opts.Events.UseMandatoryStreamTypeDeclaration = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/mandatory_stream_type_behavior.cs#L92-L104' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usemandatorystreamtypedeclaration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This causes a couple side effects that **force stricter usage of Marten**:

1. Marten will throw a `StreamTypeMissingException` exception if you call a `StartStream()` overload that doesn't include the stream type
2. Marten will throw a `NonExistentStreamException` if you try to append events to a stream that does not already exist

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
