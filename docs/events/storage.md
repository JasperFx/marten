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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L201-L210' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_event_schema' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Database Tables

The events are stored in the `mt_events` table, with these columns:

* `seq_id` - A sequential identifier, drawn from the `mt_events_sequence` sequence, that acts as the primary key
* `id` - A Guid value uniquely identifying the event across databases
* `stream_id` - A foreign key to the event stream that contains the event
* `version` - A numerical version of the event's position within its event stream
* `data` - The actual event data stored as JSONB
* `bdata` - The event data for events that use [binary serialization](/events/binary-serialization), and `NULL` for JSON events
* `type` - A string identifier for the event type that's derived from the event type name. For example, events of type `IssueResolved` would be identified as "issue_resolved." The `type`
  column exists so that Marten can be effectively used without the underlying JSON serializer having to embed type metadata.
* `timestamp` - A database timestamp written by the database when events are committed.
* `tenant_id` - Identifies the tenancy of the event
* `mt_dotnet_type` - The full name of the underlying event type, including assembly name, e.g. "Marten.Testing.Events.IssueResolved, Marten.Testing"
* `correlation_id`, `causation_id`, `headers` and `user_name` - Optional [event metadata](/events/metadata). The columns
  always exist, and are only written when the matching metadata is enabled
* `is_skipped` - Marks an event that projections and subscriptions [skip](/events/skipping)
* `is_archived` - Marks an event in an [archived](/events/archiving) stream
* `tags` - The [DCB](/events/dcb) tags of the event, only when `DcbStorageMode` is `HStore`

The "Async Daemon" projection support keys off of the sequential id, but we retained the Guid id field for backward compatibility and to retain a potential way to uniquely identify events across databases.

Streams are tracked in `mt_streams`, with one row per stream: `id`, `type` (the aggregate type name), `version`,
`timestamp`, `created`, `tenant_id`, `compacted_version` (see [stream compaction](/events/compacting)) and `is_archived`.

### Other tables

Marten always creates these tables alongside `mt_events` and `mt_streams`, whether or not you use the feature behind
each one, so that two applications sharing an event store schema never disagree about it:

| Table | What it holds |
| ----- | ------------- |
| `mt_event_progression` | The durable progress of each async projection and subscription through the event store |
| `mt_event_projection_floors` | Persisted projection start floors, which survive a rebuild's teardown |
| `mt_event_range_marks` | The ceilings and cursors of stored-event rewrites and backfills, so they can resume |
| `mt_parked_identities` | The identities an async projection has [parked](/events/projections/async-daemon#parking-a-failing-identity) after an apply error |
| `mt_high_water_skips` | The sequence ranges the high water mark skipped over |
| `mt_high_water_late_events` | Events that committed late, inside a range the high water mark had already skipped |

Others only exist when the configuration needs them:

| Object | When it exists |
| ------ | -------------- |
| `mt_streams_identity` | `opts.Events.EnableStrictStreamIdentityEnforcement` is on |
| Per-tenant `mt_events_sequence_*` sequences | `opts.Events.UseTenantPartitionedEvents` is on ([per-tenant event partitioning](/events/multitenancy#per-tenant-event-partitioning)) |
| `mt_event_tag_*` | One per registered [DCB](/events/dcb) tag type, unless `DcbStorageMode` is `HStore` |
| `mt_dcb_tag_version` and `mt_dcb_tag_version_sequence` | At least one DCB tag type is registered |
| The `hstore` extension | `DcbStorageMode` is `HStore` and at least one DCB tag type is registered |
| `mt_natural_key_*` | One per aggregate with a [natural key](/events/natural-keys) |
| `mt_slice_index_*` | One per multi-stream projection version that opts into `Options.UseSliceIndex()` |

Projections that store their own data, such as [flat table projections](/events/projections/flat), add their own tables
too.

### Functions

* `mt_quick_append_events` - Appends events to a stream in the default "Quick" append mode
* `mt_archive_stream` and `mt_unarchive_stream` - Behind `ArchiveStream()` and `UnArchiveStream()`
* `mt_mark_event_progression` and `mt_mark_progression_with_skip` - Update the `mt_event_progression` table

And lastly, there's a document type called `DeadLetterEvent` that Marten adds automatically to record information about
"dead letter events" that errored in the async daemon and were skipped in accordance with
the error handling policies in your application's Marten configuration.

## Event Metadata in Code

The columns of `mt_events` map to the members of the `IEvent` interface, which lives in the `JasperFx.Events` namespace.
`IEvent` also exposes the event's type names, its tags and whether it is archived or skipped. See
[Fetch Events for a Stream](/events/querying#fetch-events-for-a-stream) for the complete interface.

The full event data is available on `EventStream` and `IEvent` objects immediately after committing a transaction that involves event capture. See [diagnostics and instrumentation](/diagnostics) for more information on capturing event data in the instrumentation hooks.

## Event Type Names <Badge type="tip" text="8.4" />

If you look into the `mt_events` table in your system you'll see a column named `type` that will
have an alias for the .NET type name that Marten keys off when reading events from the database 
to "know" what .NET type to deserialize the JSON data to. 

The original idea was that people should be able to easily move event types around in their
solution without breaking the storage as full type names changed, so we purposely used _only_ the
type name of the .NET type for the event alias. In real life usage though, sometimes people will
use completely different .NET types with the same type name like in this example:

```csharp
public class GroupEvents
{
    public record Created(string Name);
}

public class UserEvents
{
    public record Created(string Name);
}
```

In that case, the original naming scheme of "created" will not correctly disambiguate between the
two different `Created` types above. While you _could_ manually alias all of these event types
yourself to disambiguate, it's too easy to forget to do that. Instead, you can just switch to different
naming schemes like this:

<!-- snippet: sample_event_naming_style -->
<a id='snippet-sample_event_naming_style'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // This is the default behavior, but just showing you that
    // this is an option
    opts.Events.EventNamingStyle = EventNamingStyle.ClassicTypeName;

    // This mode is "the classic style Marten has always used, except smart enough
    // to disambiguate inner classes that have the same type name"
    opts.Events.EventNamingStyle = EventNamingStyle.SmarterTypeName;

    // Forget all the pretty naming aliases, just use the .NET full type name for
    // the event type name
    opts.Events.EventNamingStyle = EventNamingStyle.FullTypeName;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/NamingStyles.cs#L13-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event_naming_style' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that you will have to switch out of the "classic" naming mode to disambiguate between event types
with the same class name in different namespaces.

## Optional Indexes

As of Marten 7.0, Marten is omitting indexes that aren't universally necessary, but
you have the option to add some extra, pre-canned indexes. Right now the only option
is to add a unique index back on the `id` column that would be useful for references to
external systems like so:

<!-- snippet: sample_using_optional_event_store_indexes -->
<a id='snippet-sample_using_optional_event_store_indexes'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // Add the unique index to the id field
    opts.Events.EnableUniqueIndexOnEventId = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/opting_into_index_on_event_id.cs#L20-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_optional_event_store_indexes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
