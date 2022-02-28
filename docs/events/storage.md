# Event Store Schema Objects

## Overriding the Schema

By default, the event store database objects are created in the default schema for the active `IDocumentStore`. If you wish,
you can segregate the event store objects into a separate schema with this syntax:

<!-- snippet: sample_setting_event_schema -->
<a id='snippet-sample_setting_event_schema'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Places all the Event Store schema objects
    // into the "events" schema
    _.Events.DatabaseSchemaName = "events";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L192-L201' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_event_schema' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Configuration

If Marten is being used in a distributed scenario, where multiple process can be appending events to the same stream at the same time, you should set `UseAppendEventForUpdateLock` to `true` on the `StoreOptions` `Events` (EventGraph) configuration. This will add a `FOR UPDATE` lock to the `mt_append_event` function that will ensure that the same stream cannot have its `version` updated by multiple processes at the same time, which could lead to a `pk_mt_events_stream_and_version` constraint violation from the `mt_events` and `mt_streams` versions getting out of sync.

## Database Tables

The events are stored in the `mt_events` table, with these columns:

* `seq_id` - A sequential identifier that acts as the primary key
* `id` - A Guid value uniquely identifying the event across databases
* `stream_id` - A foreign key to the event stream that contains the event
* `version` - A numerical version of the event's position within its event stream
* `data` - The actual event data stored as JSONB
* `type` - A string identifier for the event type that's derived from the event type name. For example, events of type `IssueResolved` would be identified as "issue_resolved." The `type`
  column exists so that Marten can be effectively used without the underlying JSON serializer having to embed type metadata.
* `timestamp` - A database timestamp written by the database when events are committed.
* `tenant_id` - Identifies the tenancy of the event
* `mt_dotnet_type` - The full name of the underlying event type, including assembly name, e.g. "Marten.Testing.Events.IssueResolved, Marten.Testing"

The "Async Daemon" projection supports keys off of the sequential id, but we retained the Guid id field for backward compatibility and to retain a potential way to uniquely identify events across databases.

In addition, there are a couple other metadata tables you'll see in your schema:

* `mt_streams` - Metadata about each event stream
* `mt_event_progression` - A durable record about the progress of each async projection through the event store

And finally, a couple functions that Marten uses internally:

* `mt_append_event` - Writes event data to the `mt_events` and `mt_streams` tables
* `mt_mark_event_progression` - Updates the `mt_event_progression` table

## Event Metadata in Code

Hopefully, it's relatively clear how the fields in `mt_events` map to the `IEvent` interface in Marten:

<!-- snippet: sample_event_metadata -->
<a id='snippet-sample_event_metadata'></a>
```cs
/// <summary>
///     A reference to the stream that contains
///     this event
/// </summary>
public Guid StreamId { get; set; }

/// <summary>
///     A reference to the stream if the stream
///     identifier mode is AsString
/// </summary>
public string? StreamKey { get; set; }

/// <summary>
///     An alternative Guid identifier to identify
///     events across databases
/// </summary>
public Guid Id { get; set; }

/// <summary>
///     An event's version position within its event stream
/// </summary>
public long Version { get; set; }

/// <summary>
///     A global sequential number identifying the Event
/// </summary>
public long Sequence { get; set; }

/// <summary>
///     The UTC time that this event was originally captured
/// </summary>
public DateTimeOffset Timestamp { get; set; }

public string TenantId { get; set; } = Tenancy.DefaultTenantId;

/// <summary>
/// Optional metadata describing the causation id
/// </summary>
public string? CausationId { get; set; }

/// <summary>
/// Optional metadata describing the correlation id
/// </summary>
public string? CorrelationId { get; set; }

/// <summary>
/// This is meant to be lazy created, and can be null
/// </summary>
public Dictionary<string, object>? Headers { get; set; }
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Event.cs#L127-L177' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The full event data is available on `EventStream` and `IEvent` objects immediately after committing a transaction that involves event capture. See [diagnostics and instrumentation](/diagnostics) for more information on capturing event data in the instrumentation hooks.
