<!--Title:Event Store Schema Objects-->

## Overriding the Schema

By default, the event store database objects are created in the default schema for the active `IDocumentStore`. If you wish,
you can segregate the event store objects into a separate schema with this syntax:

<[sample:setting_event_schema]>

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

<[sample:event_metadata]>

The full event data is available on `EventStream` and `IEvent` objects immediately after committing a transaction that involves event capture. See <[linkto:documentation/documents/diagnostics]> for more information on capturing event data in the instrumentation hooks.
