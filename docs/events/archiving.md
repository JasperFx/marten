# Archiving Event Streams

Like most (all?) event stores, Marten is designed around the idea of the events being persisted to a single file, immutable
log of events. All the same though, there are going to be problem domains where certain event streams become obsolete. Maybe
because a workflow is completed, maybe through time based expiry rules, or maybe because a customer or user is removed
from the system. To help optimize Marten's event store usage, you can take advantage of the stream archiving to 
mark events as archived on a stream by stream basis. 

::: warning
You can obviously use pure SQL to modify the events persisted by Marten. While that might be valuable in some cases,
we urge you to be cautious about doing so.
:::

The impact of archiving an event stream is:

* In the "classic" usage of Marten, the relevant stream and event rows are marked with an `is_archived = TRUE`
* With the "opt in" table partitioning model for "hot/cold" storage described in the next section, the stream and event rows are
  moved to the archived partition tables for streams and events
* The [async daemon](/events/projections/async-daemon) subsystem process that processes projections and subscriptions in a background process automatically ignores
  archived events -- but that can be modified on a per projection/subscription basis
* Archived events are excluded by default from any event data queries through the LINQ support in Marten

To mark a stream as archived, it's just this syntax:

<!-- snippet: sample_archive_stream_usage -->
<a id='snippet-sample_archive_stream_usage'></a>
```cs
public async Task SampleArchive(IDocumentSession session, string streamId)
{
    session.Events.ArchiveStream(streamId);
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L28-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_archive_stream_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As in all cases with an `IDocumentSession`, you need to call `SaveChanges()` to commit the
unit of work.

::: tip
At this point, you will also have to manually delete any projected aggregates based on the event streams being
archived if that is desirable
:::

The `mt_events` and `mt_streams` tables both have a boolean column named `is_archived`.

Archived events are filtered out of all event Linq queries by default. But of course, there's a way
to query for archived events with the `IsArchived` property of `IEvent` as shown below:

<!-- snippet: sample_querying_for_archived_events -->
<a id='snippet-sample_querying_for_archived_events'></a>
```cs
var events = await theSession.Events
    .QueryAllRawEvents()
    .Where(x => x.IsArchived)
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L228-L235' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_for_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also query for all events both archived and not archived with `MaybeArchived()`
like so:

<!-- snippet: sample_query_for_maybe_archived_events -->
<a id='snippet-sample_query_for_maybe_archived_events'></a>
```cs
var events = await theSession.Events.QueryAllRawEvents()
    .Where(x => x.MaybeArchived()).ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L263-L268' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_maybe_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Hot/Cold Storage Partitioning <Badge type="tip" text="7.25" />

::: warning
This option will only be beneficial if you are being aggressive about marking obsolete, old, or expired event data
as archived.
:::

Want your system using Marten to scale and perform even better than it already does? If you're leveraging
event archiving in your application workflow, you can possibly derive some significant performance and scalability
improvements by opting into using PostgreSQL native table partitioning on the event and event stream data
to partition the "hot" (active) and "cold" (archived) events into separate partition tables. 

The long and short of this option is that it keeps the active `mt_streams` and `mt_events` tables smaller, which pretty
well always results in better performance over time.

The simple flag for this option is:

<!-- snippet: sample_turn_on_stream_archival_partitioning -->
<a id='snippet-sample_turn_on_stream_archival_partitioning'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // Turn on the PostgreSQL table partitioning for
    // hot/cold storage on archived events
    opts.Events.UseArchivedStreamPartitioning = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/Optimizations.cs#L13-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_turn_on_stream_archival_partitioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
If you are turning this option on to an existing system, you may want to run the database schema migration script
by hand rather than trying to let Marten do it automatically. The data migration from non-partitioned to partitioned
will probably require system downtime because it actually has to copy the old table data, drop the old table, create the new 
table, copy all the existing data from the temp table to the new partitioned table, and finally drop the temporary table.
:::
