<!--Title:Appending Events-->
<!--Url:appending-->

Marten's event sourcing support "appends" event data documents to a single table `mt_events.` Events must be captured against a stream id, with a second table called `mt_streams` that Marten uses to
keep metadata describing the state of an individual stream. Appending events to either a new or existing stream is done within the same Marten transaction as any other document updates or deletions. See 
<[linkto:documentation/documents/basics/persisting]> for more information on Marten transactions.

## Event Types

The only requirement that Marten makes on types used as events is that they are:

1. Public, concrete types
1. Can be bidirectionally serialized and deserialized with a tool like Newtonsoft.Json

Marten does need to know what the event types are before you issue queries against the event data (it's just to handle the deserialization from JSON). The event registration will happen automatically when you append events,
but for production usage when you may be querying event data before you append anything, you just need to register the event types upfront like this:

<[sample:registering-event-types]>


## Stream or Aggregate Types

At this point there are no specific requirements about stream aggregate types as they are purely marker types for now. In the future we will probably support
aggregating events via snapshot caching using the aggregate type.


## Starting a new Stream

As of Marten v0.9, you can **optionally** start a new event stream against some kind of .Net type that theoretically marks the type of stream your capturing. 
Marten does not yet use this type as anything more than metadata, but our thought is that some projections would key off this information and that in a 
future version use that aggregate type to perform versioned snapshots of the entire stream. We may also make the aggregate type optional so that
you could just supply either a string to mark the "stream type" or work without a stream type.

As usual, our sample problem domain is the Lord of the Rings style "Quest." For now, you can either start a new stream and let Marten assign the Guid
id for the stream:

<[sample:start-stream-with-aggregate-type]>

Or have Marten use a Guid value that you provide yourself:

<[sample:start-stream-with-existing-guid]>

For stream identity (strings vs. Guids), see <[linkto:documentation/events/identity]>.

Note that `StartStream` checks for existing stream and throws `ExistingStreamIdCollisionException` in case stream already exists.

## Appending Events

If you have an existing stream, you can later append additional events with `IEventStore.Append()` as shown below:

<[sample:append-events]>

### Appending & Assertions ###

`IEventStore.Append()` supports an overload taking in a parameter `int expectedVersion` that can be used to assert that events are inserted into the
event stream if and only if the maximum event id for the stream matches the expected version after event insertions. Otherwise the transaction is aborted
and a `EventStreamUnexpectedMaxEventIdException` exception is thrown.

<[sample:append-events-assert-on-eventid]> 

### CreateStream vs. Append

Both, `CreateStream` and `Append` can be used to start a new event stream. The difference with the methods is that `CreateStream` always checks for existing stream and throws `ExistingStreamIdCollisionException` in case stream already exists.
