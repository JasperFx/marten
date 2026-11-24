# Rewriting Stored Events <Badge type="tip" text="10.0" />

Events are immutable, and most of the time that is exactly right. But how an event is *stored* can fall behind how
the application would store it today:

- an [upcaster](/events/versioning#upcasting-advanced-payload-transformations) reads an old shape on every read,
  forever, because the old rows never change;
- an event type moved to [binary serialization](/events/binary-serialization), but everything appended before that is still JSON;
- a serializer that encrypts personal data was introduced, but the history is still in plain text;
- a payload carries members its record dropped long ago.

`RewriteStoredEventsAsync` reads the selected events the way Marten reads them (upcasters, binary or JSON, the
configured serializer) and writes each one back the way an append would store it today: under its current type
name, binary or JSON as now configured, through the configured serializer.

<!-- snippet: sample_rewriting_one_event_type -->
<a id='snippet-sample_rewriting_one_event_type'></a>
```cs
var result = await store.Advanced.RewriteStoredEventsAsync(x =>
{
    // Every event read as PersonRegistered, including those an upcaster reads from an older name
    x.IncludeType<PersonRegistered>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L25-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_one_event_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Once a type has been rewritten in every database, an upcaster that only existed for its old rows can be removed.

`IncludeAllTypes()` selects every event in the store instead. Archived events are history too, so they are rewritten
like any other. Events of a type the application no longer knows are left as they are and counted as `UnknownTypes`.

To change a few individual events inside a session instead, see
[Changing single events in a session](#changing-single-events-in-a-session).

## Correcting events

::: warning
A transform rewrites history. The old version of each event is gone once the rewrite has written it, and nothing
that was built from the old version changes with it: projections that already processed the event, natural keys
and tags keep what they had until you rebuild them. Take a backup first, and try it with `DryRun` on a copy.
:::

A transform changes the events of one type before they are written back:

<!-- snippet: sample_rewriting_with_a_transform -->
<a id='snippet-sample_rewriting_with_a_transform'></a>
```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
    x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() }));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L38-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_with_a_transform' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Use this for corrections the event's readers can live with in either form: until the rewrite completes, old and new
rows exist side by side. A change of shape belongs in an upcaster, which the rewrite then makes permanent.

## Only events still stored as JSON

Some serializers never write the same bytes twice for the same event, for example one that encrypts with a random
IV. Every row then looks changed, and every run writes everything again. When the point of the rewrite is to move
events from JSON to such a serializer, read only the rows that are still JSON:

<!-- snippet: sample_rewriting_only_events_stored_as_json -->
<a id='snippet-sample_rewriting_only_events_stored_as_json'></a>
```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<PersonRegistered>();
    x.OnlyEventsStoredAsJson();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L48-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_only_events_stored_as_json' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## What it does to the database

A rewrite updates rows of the largest table in the store, so it is never started for you; you start it, and decide
how hard it may push:

<!-- snippet: sample_rewriting_throttling -->
<a id='snippet-sample_rewriting_throttling'></a>
```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<ClaimImported>();

    // Events read per page and written back in one statement
    x.BatchSize = 500;

    // Databases rewritten at the same time, for a multi-database store
    x.MaxParallelDatabases = 1;

    // Room for the application between pages
    x.PauseBetweenBatches = 100.Milliseconds();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L61-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_throttling' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- **Only events that change are written.** The comparison happens in the database, so a row that would come out the
  same leaves no dead tuple behind, and running a rewrite again costs a read of the selected events.
- **Every page is its own short statement.** Nothing holds locks across pages.
- **A row changed by someone else in the meantime is skipped, not overwritten.** The result counts these as
  `Skipped`; run the rewrite again to include them.
- **Updated rows leave dead tuples and write-ahead log.** Expect WAL of a few times the rewritten payload, and a
  table that only shrinks after `VACUUM FULL` or `pg_repack`. Use `PauseBetweenBatches` to keep replication lag and
  I/O in check.

A dry run reads and compares everything but writes nothing, and reports how many events would change and their
payload size before and after:

<!-- snippet: sample_rewriting_dry_run -->
<a id='snippet-sample_rewriting_dry_run'></a>
```cs
var result = await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<ClaimImported>();
    x.DryRun = true;
});

foreach (var database in result.Databases)
{
    Console.WriteLine($"{database.Cursor.Database}: {database.Rewritten} of {database.Visited} events, " +
                      $"{database.BytesBefore} -> {database.BytesAfter} bytes");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L82-L96' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_dry_run' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## One database or one tenant

By default a rewrite covers every database of the store. To do one at a time, name the database by its identifier,
or name a tenant. A tenant means its database, and with conjoined event tenancy only that tenant's rows:

<!-- snippet: sample_rewriting_one_tenant_or_one_database -->
<a id='snippet-sample_rewriting_one_tenant_or_one_database'></a>
```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<ClaimImported>();
    x.ForTenant("tenant-b");
});

await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<ClaimImported>();
    x.ForDatabase(database.Identifier);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L101-L115' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_one_tenant_or_one_database' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Progress and resuming

Events are visited in order of sequence, then tenant, because a sequence alone is not unique with
[per-tenant event partitions](/events/multitenancy). After every page `OnProgress` reports the cursor; pass it to
`ResumeFrom` to continue a database where an earlier run stopped:

<!-- snippet: sample_rewriting_resuming -->
<a id='snippet-sample_rewriting_resuming'></a>
```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<ClaimImported>();
    x.ResumeFrom(lastCursor);
    x.OnProgress = (progress, ct) => saveCursorAsync(progress.Cursor, ct);
}, cancellationToken);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L123-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_resuming' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Cancelling the token stops the rewrite. Each page is written in one statement, so a page is written whole or not at
all, and the last cursor `OnProgress` reported is where to pick it up again. The rewrite keeps no state of its own and
needs no running host or async daemon: where the cursor is kept, and when the rewrite runs, is up to you.

Because unchanged events are never written, starting over from the beginning is always safe, only slower.

`StopAfter(sequence)` leaves the events after that sequence alone. Take the sequence when you decide to run the
rewrite, so a transform never touches events appended after that:

<!-- snippet: sample_rewriting_stop_after -->
<a id='snippet-sample_rewriting_stop_after'></a>
```cs
var ceiling = await store.Storage.Database.FetchHighestEventSequenceNumber();

await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() });
    x.StopAfter(ceiling);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L137-L147' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_stop_after' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Running a rewrite as a job

Marten also implements `IEventStore.RewriteStoredEventsAsync` from JasperFx, which a Wolverine job uses to run a
rewrite one database at a time. A transform used that way is registered by name on `opts.Events`:

<!-- snippet: sample_rewriting_named_transform -->
<a id='snippet-sample_rewriting_named_transform'></a>
```cs
opts.Events.AddRewriteTransform<PersonMoved>("upper-case-city",
    e => e with { City = e.City.ToUpperInvariant() });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Rewriting/rewriting_documentation_samples.cs#L152-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewriting_named_transform' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The job's ceiling and cursor are kept in the `mt_event_range_marks` table. Each page is written in one transaction
with its cursor, so a job that crashes and resumes never sends an event through a transform twice. Under per-tenant
event partitions, a job for the whole database goes through the tenants one after another, each with its own ceiling
and cursor.

## Changing single events in a session {#changing-single-events-in-a-session}

`RewriteStoredEventsAsync` works through whole event types, page by page. To change a handful of events you have
already found, `IDocumentSession.Events` has two targeted operations instead. Both only queue an update of the event's
row in `mt_events`: nothing is written until `SaveChangesAsync()`, and the update is part of that transaction.

`OverwriteEvent(IEvent)` writes back an event you loaded, after you changed its body or its headers:

<!-- snippet: sample_overwrite_event -->
<a id='snippet-sample_overwrite_event'></a>
```cs
var events = await session.Events.FetchStreamAsync(ticketId);

foreach (var opened in events.OfType<Event<ApiDocsTicketOpened>>())
{
    // Change the body of the event you loaded...
    opened.Data = opened.Data with { ReporterEmail = "redacted" };

    // ...and its headers, which are only written when headers are enabled
    opened.SetHeader("redacted-by", "support");

    session.Events.OverwriteEvent(opened);
}

// Nothing is written until SaveChangesAsync()
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/EventStoreApiDocsSamples.cs#L149-L167' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overwrite_event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It finds the row by the event's `Sequence` and rewrites its data, its type name and .NET type (those of the event's
current type, so an event an upcaster read from an older name is stored under the new one) and, when
[headers](/events/metadata) are enabled, its headers. The id, version, stream, timestamp, tenant, and the causation,
correlation and user name columns stay as they were. This is the operation
[`ApplyEventDataMasking()`](/events/protection) uses for every event a masking rule matched.

`CompletelyReplaceEvent<T>(sequence, body)` puts a new body, which may be of another event type, in place of the event
at that sequence:

<!-- snippet: sample_completely_replace_event -->
<a id='snippet-sample_completely_replace_event'></a>
```cs
var events = await session.Events.FetchStreamAsync(ticketId);
var last = events[^1];

// The replacement can be of a different event type than the original. Marten
// returns the new id it assigns to the event
var newEventId = session.Events.CompletelyReplaceEvent(last.Sequence,
    new ApiDocsTicketClosed("Closed by an administrator"));

await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/EventStoreApiDocsSamples.cs#L172-L184' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_completely_replace_event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The stream and version stay the same. The event gets a new id, which the method returns, and a new timestamp, and its
headers are emptied and its causation id, correlation id and user name cleared, for whichever of those are enabled.
This is the operation [stream compacting](/events/compacting) uses to put the `Compacted<T>` snapshot in place of the
stream's last event.

Both store the event the way an append stores it today. An event type that uses
[binary serialization](/events/binary-serialization) is written to the binary column, with an empty JSON object in the
JSON column, and a JSON event clears the binary column, so a row is never read back through an old binary payload.
Before Marten 10 ([#5642](https://github.com/JasperFx/marten/issues/5642)) both operations always wrote the body as JSON and left the binary column alone, and
`OverwriteEvent()` left the type name as it was.

::: warning
Neither operation re-runs anything that was built from the old event. Inline and asynchronous projections, natural keys
and tags keep what they had until you rebuild them, and an asynchronous projection that has already passed the event's
sequence never sees the change. With conjoined event tenancy, open the session for the event's tenant: only that tenant's row with
the sequence is updated.
:::
