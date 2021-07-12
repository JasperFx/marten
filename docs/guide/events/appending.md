# Appending Events

Marten's event sourcing support "appends" event data documents to a single table `mt_events.` Events must be captured against a stream id, with a second table called `mt_streams` that Marten uses to
keep metadata describing the state of an individual stream. Appending events to either a new or existing stream is done within the same Marten transaction as any other document updates or deletions. See
[persisting documents](/guide/documents/basics/persisting) for more information on Marten transactions.

## Event Types

The only requirement that Marten makes on types used as events is that they are:

1. Public, concrete types
1. Can be bidirectionally serialized and deserialized with a tool like Newtonsoft.Json

Marten does need to know what the event types are before you issue queries against the event data (it's just to handle the de-serialization from JSON). The event registration will happen automatically when you append events,
but for production usage when you may be querying event data before you append anything, you just need to register the event types upfront like this:

<!-- snippet: sample_registering-event-types -->
<a id='snippet-sample_registering-event-types'></a>
```cs
var store2 = DocumentStore.For(_ =>
{
    _.DatabaseSchemaName = "samples";
    _.Connection(ConnectionSource.ConnectionString);
    _.AutoCreateSchemaObjects = AutoCreate.None;

    _.Events.AddEventType(typeof(QuestStarted));
    _.Events.AddEventType(typeof(MonsterSlayed));
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/using_the_schema_objects_Tests.cs#L35-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering-event-types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Stream or Aggregate Types

At this point there are no specific requirements about stream aggregate types as they are purely marker types. In the future we will probably support aggregating events via snapshot caching using the aggregate type.

## Starting a new Stream

As of Marten v0.9, you can **optionally** start a new event stream against some kind of .Net type that theoretically marks the type of stream you're capturing. Marten does not yet use this type as anything more than metadata, but our thought is that some projections would key off this information and in a future version use that aggregate type to perform versioned snapshots of the entire stream. We may also make the aggregate type optional so that you could just supply either a string to mark the "stream type" or work without a stream type.

As usual, our sample problem domain is the Lord of the Rings style "Quest." For now, you can either start a new stream and let Marten assign the Guid id for the stream:

<!-- snippet: sample_start-stream-with-aggregate-type -->
<a id='snippet-sample_start-stream-with-aggregate-type'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream<Quest>(joined, departed).Id;
session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L59-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-1'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream<Quest>(joined, departed).Id;
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L90-L96' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-1' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-2'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream<Quest>(joined, departed).Id;
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L121-L127' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-2' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-3'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream<Quest>(joined, departed).Id;
session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L153-L159' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-3' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-4'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream(joined, departed).Id;
session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests.cs#L35-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-4' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-5'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream(joined, departed).Id;
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests.cs#L63-L69' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-5' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-6'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream(joined, departed).Id;
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests.cs#L91-L97' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-6' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-7'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = session.Events.StartStream(joined, departed).Id;
session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests.cs#L119-L125' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-7' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-8'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = "Second";
session.Events.StartStream<Quest>(id, joined, departed);
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers.cs#L55-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-8' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-9'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = "Third";
session.Events.StartStream<Quest>(id, joined, departed);
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers.cs#L83-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-9' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-aggregate-type-10'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = "Fourth";
session.Events.StartStream<Quest>(id, joined, departed);
session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers.cs#L112-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-aggregate-type-10' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or have Marten use a Guid value that you provide yourself:

<!-- snippet: sample_start-stream-with-existing-guid -->
<a id='snippet-sample_start-stream-with-existing-guid'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = Guid.NewGuid();
session.Events.StartStream<Quest>(id, joined, departed);
session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L362-L369' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-existing-guid' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_start-stream-with-existing-guid-1'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

var id = Guid.NewGuid();
session.Events.StartStream(id, joined, departed);
session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests.cs#L300-L307' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start-stream-with-existing-guid-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For stream identity (strings vs. Guids), see [here](/guide/events/identity).

Note that `StartStream` checks for an existing stream and throws `ExistingStreamIdCollisionException` if a matching stream already exists.

## Appending Events

If you have an existing stream, you can later append additional events with `IEventStore.Append()` as shown below:

<!-- snippet: sample_append-events -->
<a id='snippet-sample_append-events'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

session.Events.Append(id, joined, departed);

session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L567-L574' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append-events' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_append-events-1'></a>
```cs
var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

session.Events.Append(id, joined, departed);

session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests.cs#L483-L490' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append-events-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Appending & Assertions ###

`IEventStore.Append()` supports an overload taking in a parameter `int expectedVersion` that can be used to assert that events are inserted into the event stream if and only if the maximum event id for the stream matches the expected version after event insertions. Otherwise the transaction is aborted and an `EventStreamUnexpectedMaxEventIdException` exception is thrown.

<!-- snippet: sample_append-events-assert-on-eventid -->
<a id='snippet-sample_append-events-assert-on-eventid'></a>
```cs
session.Events.StartStream<Quest>(id, started);
session.SaveChanges();

var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

// Events are appended into the stream only if the maximum event id for the stream
// would be 3 after the append operation.
session.Events.Append(id, 3, joined, departed);

session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_Tests.cs#L606-L618' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append-events-assert-on-eventid' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_append-events-assert-on-eventid-1'></a>
```cs
session.Events.StartStream(id, started);
session.SaveChanges();

var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
var departed = new MembersDeparted { Members = new[] { "Thom" } };

// Events are appended into the stream only if the maximum event id for the stream
// would be 3 after the append operation.
session.Events.Append(id, 3, joined, departed);

session.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/end_to_end_event_capture_and_fetching_the_stream_with_non_typed_streams_Tests.cs#L516-L528' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append-events-assert-on-eventid-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### StartStream vs. Append

Both `StartStream` and `Append` can be used to start a new event stream. The difference with the methods is that `StartStream` always checks for existing stream and throws `ExistingStreamIdCollisionException` in case the stream already exists.
