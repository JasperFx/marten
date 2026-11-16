# Cold Event Storage

Finished streams are often the bulk of an event store, but they still have to stay readable: a correction
years later folds the aggregate again and appends to it. Marten lets an application move the first part of
a stream out of `mt_events` into storage it owns, and reads that *cold* part back whenever it folds the
stream live.

This is not the same as [archiving](/events/archiving): an archived stream stays in PostgreSQL and is
closed for appends, while a cold stream keeps working as an ordinary stream.

::: warning Experimental
Cold event storage is experimental, and its API may still change. Every type and method of it lives in
`Marten.Events.Cold` and is marked `[Experimental("MARTEN001")]`, so using it is a compiler error until you
opt in with `#pragma warning disable MARTEN001`, or `<NoWarn>$(NoWarn);MARTEN001</NoWarn>` in the project
file.

Read [What does not read the cold part](#what-does-not-read-the-cold-part) before you move anything.
:::

## Configuration

Implement `IColdEventStore` and register it. Cold event storage requires string stream identity:

```cs
using Marten.Events.Cold;

opts.Events.StreamIdentity = StreamIdentity.AsString;
opts.Events.UseColdEventStore(new MyColdEventStore());

public class MyColdEventStore: IColdEventStore
{
    public async IAsyncEnumerable<ColdEvent> ReadAsync(IQuerySession session, string streamKey,
        long throughVersion, [EnumeratorCancellation] CancellationToken token)
    {
        // Read from your own storage, for session.TenantId.
        // Oldest first, at most one event per version, none above throughVersion.
        yield break;
    }
}
```

A `ColdEvent` carries what `mt_events` held for the event: id, version, sequence, the stored event type
name and .NET type name, timestamp, the JSON (or binary) payload, headers, correlation and causation id,
user name and tags.

`ReadAsync` must return nothing above `throughVersion`, even when your storage holds more of the stream:
Marten asks for exactly the part it does not read from `mt_events`, and may ask for less than the whole cold
part.

With a cold event store configured, `mt_streams` gets a nullable `cold_version` column through the normal
schema migration. Stores without one see no schema change.

## Moving a stream's events

Which streams go cold, and when, is up to the application. You copy the events to your cold storage, and
`MarkStreamCold` deletes them from `mt_events`:

```cs
// copy versions 1..5 of the stream to your cold storage, then:
session.Events.MarkStreamCold(streamKey, 5);
await session.SaveChangesAsync();
```

`MarkStreamCold` deletes the stream's events through that version from `mt_events` (and their tag rows),
and records the version in `mt_streams.cold_version`. It fails the unit of work if the stream does not exist
or has not reached that version. `mt_streams.version` is left alone, so optimistic concurrency keeps working
as before.

### In which order to copy and mark

How the copy and the mark go together depends on where the cold storage lives:

- **In the same PostgreSQL database**: write the copy in the same unit of work as `MarkStreamCold`. Both
  commit or neither does.
- **Anywhere else** (another database, object storage): copy first, and call `MarkStreamCold` only once
  the copy is durable. The other order loses events when the copy fails after the delete committed.

In the second case, and in the first when you retry, the same events can be copied more than once: a move
can be refused (see below), fail after the copy, or be retried by your own job. Make the copy idempotent,
for example by keying it on stream and version and overwriting. Until `MarkStreamCold` commits, the events
are in both places; that is harmless, because Marten reads the cold store only up to `mt_streams.cold_version`
and `mt_events` only above it.

### Moving again as a stream grows

A cold stream stays an ordinary stream. New events are appended to `mt_events` as usual, with the version
continuing after the cold part. Once those are finished too, move them with a higher version, and repeat as
often as the stream grows:

```cs
// versions 1..5 went cold earlier; 6 and 7 were appended since
session.Events.MarkStreamCold(streamKey, 7);
```

The boundary only moves up. Marking a stream cold through a version at or below its current boundary
deletes nothing and changes nothing, so retrying a move is harmless.

::: warning
The async daemon does not read cold events. `MarkStreamCold` therefore refuses, deleting nothing, while any
registered async projection or subscription has not processed the events it would delete: when its
`mt_event_progression` row for the tenant (or the store-global row without `UseTenantPartitionedEvents`) is
below the highest sequence being deleted, or missing. Move streams once the daemon has caught up, and retry
the ones that were refused. A projection or subscription that is registered but never runs blocks every move.
:::

## Reading

- `FetchForWriting`, `FetchForExclusiveWriting`, `FetchLatest` (also in a batch) and `AggregateStreamAsync`
  fold the cold events first and then the events in `mt_events`, for live aggregations. A stream with only
  cold events comes back whole, at its current version, so appending to it is ordinary code.
- Cold events are deserialized exactly like events from `mt_events`: the same event type resolution
  (aliases, the stored .NET type as fallback) and the same upcasters. `store.Events.DeserializeAsync(ColdEvent)`
  exposes this for reading cold events yourself.

## What does not read the cold part

Only the live folds above read the cold event store. Everything else sees `mt_events` alone, and on a cold
stream that is **silently wrong**, without an error:

| Operation | On a cold stream |
|---|---|
| Inline and async aggregations (snapshots) | Correct only while the stored snapshot was built before the events went cold. A stream whose snapshot is missing starts again from its warm events. |
| Rebuilding a projection | Replays `mt_events` only, so every snapshot and read model of a cold stream loses its cold part. |
| A projection registered after streams went cold | Same as a rebuild: it never sees the cold events. |
| `CompactStreamAsync` | Builds its `Compacted<T>` snapshot from the warm events only. Later folds jump to that snapshot and drop the cold part for good. **Do not compact a cold stream.** |
| `FetchStreamAsync`, `QueryAllRawEvents()` | Return the events in `mt_events` only. |

Before you move a stream, make sure nothing in this table will run on it, or that you can live with the
result.

## Knowing that an append continues a cold stream

`await session.Events.ColdVersionOfAsync(streamAction)` answers, for a stream the unit of work appends to,
its cold version, so an `IDocumentSessionListener` can update the application's own bookkeeping in the same
transaction, whether the events were appended after `FetchForWriting` or with a plain `Append`, and through
`ForTenant()` as well. The first call looks up every appended stream the unit of work has not fetched in one
query; a session that never asks pays nothing. It also answers for a stream the unit of work does not append
to, with one query for that stream.

Every appended stream whose cold version the unit of work observed, through this call or a live fetch, is
checked again at commit, behind the appends' own row locks. If another transaction moved more of it to cold
storage in between, the commit fails with `ColdStreamChangedException` and nothing is written; retry the unit
of work.
