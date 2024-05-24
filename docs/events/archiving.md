# Archiving Event Streams

New in Marten V4 is the ability to mark an event stream and all of its events as "archived." While
in the future this may have serious optimization benefits when Marten is able to utilize
Postgresql sharding, today it's metadata and default filtering in the Linq querying against event
data as well as asynchronous projections inside of the [async daemon](/events/projections/async-daemon).

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L27-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_archive_stream_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As in all cases with an `IDocumentSession`, you need to call `SaveChanges()` to commit the
unit of work.

The `mt_events` and `mt_streams` tables now both have a boolean column named `is_archived`.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L166-L173' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_for_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also query for all events both archived and not archived with `MaybeArchived()`
like so:

<!-- snippet: sample_query_for_maybe_archived_events -->
<a id='snippet-sample_query_for_maybe_archived_events'></a>
```cs
var events = await theSession.Events.QueryAllRawEvents()
    .Where(x => x.MaybeArchived()).ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L197-L202' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_maybe_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
