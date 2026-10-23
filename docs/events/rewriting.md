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

## Correcting events

A transform changes the events of one type before they are written back:

```cs
await store.Advanced.RewriteStoredEventsAsync(x =>
    x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() }));
```

Use this for corrections the event's readers can live with in either form: until the rewrite completes, old and new
rows exist side by side, and projections that already processed an event do not see the new version unless they are
rebuilt. A change of shape belongs in an upcaster, which the rewrite then makes permanent.

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
});
```

Because unchanged events are never written, starting over from the beginning is always safe, only slower.
