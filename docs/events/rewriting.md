# Rewriting Stored Events

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

```cs
var result = await store.Advanced.RewriteStoredEventsAsync(x =>
{
    // Every event read as PersonRegistered, including those an upcaster reads from an older name
    x.IncludeType<PersonRegistered>();
});
```

Once a type has been rewritten in every database, an upcaster that only existed for its old rows can be removed.

`IncludeAllTypes()` selects every event in the store instead. Archived events are history too, so they are rewritten
like any other. Events of a type the application no longer knows are left as they are and counted as `UnknownTypes`.

## Correcting events

::: warning
A transform rewrites history. The old version of each event is gone once the rewrite has written it, and nothing
that was built from the old version changes with it: projections that already processed the event, natural keys
and tags keep what they had until you rebuild them. Take a backup first, and try it with `DryRun` on a copy.
:::

A transform changes the events of one type before they are written back:

```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
    x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() }));
```

Use this for corrections the event's readers can live with in either form: until the rewrite completes, old and new
rows exist side by side. A change of shape belongs in an upcaster, which the rewrite then makes permanent.

## Only events still stored as JSON

Some serializers never write the same bytes twice for the same event, for example one that encrypts with a random
IV. Every row then looks changed, and every run writes everything again. When the point of the rewrite is to move
events from JSON to such a serializer, read only the rows that are still JSON:

```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<PersonRegistered>();
    x.OnlyEventsStoredAsJson();
});
```

## What it does to the database

A rewrite updates rows of the largest table in the store, so it is never started for you; you start it, and decide
how hard it may push:

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

## One database or one tenant

By default a rewrite covers every database of the store. To do one at a time, name the database by its identifier,
or name a tenant. A tenant means its database, and with conjoined event tenancy only that tenant's rows:

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

## Progress and resuming

Events are visited in order of sequence, then tenant, because a sequence alone is not unique with
[per-tenant event partitions](/events/multitenancy). After every page `OnProgress` reports the cursor; pass it to
`ResumeFrom` to continue a database where an earlier run stopped:

```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.IncludeType<ClaimImported>();
    x.ResumeFrom(lastCursor);
    x.OnProgress = (progress, ct) => saveCursorAsync(progress.Cursor, ct);
}, cancellationToken);
```

Cancelling the token stops the rewrite. Each page is written in one statement, so a page is written whole or not at
all, and the last cursor `OnProgress` reported is where to pick it up again. The rewrite keeps no state of its own and needs no running host or async daemon: where the cursor is kept,
and when the rewrite runs, is up to you.

Because unchanged events are never written, starting over from the beginning is always safe, only slower.

`StopAfter(sequence)` leaves the events after that sequence alone. Take the sequence when you decide to run the
rewrite, so a transform never touches events appended after that:

```cs
var ceiling = await store.Storage.Database.FetchHighestEventSequenceNumber();

await store.Advanced.RewriteStoredEventsAsync(x =>
{
    x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() });
    x.StopAfter(ceiling);
});
```

## Running a rewrite as a job

Marten also implements `IEventStore.RewriteStoredEventsAsync` from JasperFx, which a Wolverine job uses to run a
rewrite one database at a time. A transform used that way is registered by name:

```cs
opts.EventGraph.AddRewriteTransform<PersonMoved>("upper-case-city",
    e => e with { City = e.City.ToUpperInvariant() });
```

The job's ceiling and cursor are kept in the `mt_event_range_marks` table. Each page is written in one transaction
with its cursor, so a job that crashes and resumes never sends an event through a transform twice. Under per-tenant
event partitions, a job for the whole database goes through the tenants one after another, each with its own ceiling
and cursor.
