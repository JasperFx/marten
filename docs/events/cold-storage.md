# Cold Event Storage

Finished streams are often the bulk of an event store, but they still have to stay readable: a correction
years later folds the aggregate again and appends to it. Marten lets an application move the first part of
a stream out of `mt_events` into storage it owns, and reads that *cold* part back whenever it folds the
stream.

This is not the same as [archiving](/events/archiving): an archived stream stays in PostgreSQL and is
closed for appends, while a cold stream keeps working as an ordinary stream.

## Configuration

Implement `IColdEventStore` and register it:

```cs
opts.Events.UseColdEventStore(new MyColdEventStore());

public class MyColdEventStore: IColdEventStore
{
    // Implement the overload that matches your StreamIdentity
    public async IAsyncEnumerable<ColdEvent> ReadAsync(IQuerySession session, string streamKey,
        long throughVersion, [EnumeratorCancellation] CancellationToken token)
    {
        // Read from your own tables, through session.TenantId and the session's database.
        // Oldest first, at most one event per version, none above throughVersion.
        yield break;
    }
}
```

A `ColdEvent` carries what `mt_events` held for the event: id, version, sequence, the stored event type
name and .NET type name, timestamp, the JSON (or binary) payload, headers, correlation and causation id,
user name and tags.

With a cold event store configured, `mt_streams` gets a nullable `cold_version` column through the normal
schema migration. Stores without one see no schema change.

## Moving a stream's events

Which streams go cold, and when, is up to the application. Copy the events to your cold storage and mark
the stream in the same unit of work:

```cs
// copy versions 1..5 of the stream to your own tables, then:
session.Events.MarkStreamCold(streamKey, 5);
await session.SaveChangesAsync();
```

`MarkStreamCold` deletes the stream's events through that version from `mt_events` (and their tag rows),
and records the version in `mt_streams.cold_version`. It fails the unit of work if the stream does not exist
or has not reached that version. `mt_streams.version` is left alone, so optimistic concurrency keeps working
as before.

A cold stream stays an ordinary stream. New events are appended to `mt_events` as usual, with the version
continuing after the cold part. Once those are finished too, move them with a higher version, and repeat as
often as the stream grows:

```cs
// versions 1..3 went cold earlier; 4 and 5 were appended since
session.Events.MarkStreamCold(streamKey, 5);
```

The boundary only moves up. Marking a stream cold through a version at or below its current boundary
deletes nothing and changes nothing, so retrying a move is harmless. Events below the boundary are always
read from the cold event store, never from `mt_events`.

::: warning
The async daemon does not read cold events. `MarkStreamCold` therefore refuses, deleting nothing, while any
registered async projection or subscription has not processed the events it would delete: when its
`mt_event_progression` row for the tenant (or the store-global row without `UseTenantPartitionedEvents`) is
below the highest sequence being deleted, or missing. Move streams once the daemon has caught up, and retry
the ones that were refused. A projection or subscription that is registered but never runs blocks every move.
:::

## Reading

- `FetchForWriting`, `FetchForExclusiveWriting`, `FetchLatest` and `AggregateStreamAsync` fold the cold
  events first and then the events in `mt_events`, for live aggregations. A stream with only cold events
  comes back whole, at its current version, so appending to it is ordinary code.
- `FetchStreamAsync` still returns the events in `mt_events` only. `FetchStreamIncludingColdAsync` returns
  the cold part first.
- Cold events are deserialized exactly like events from `mt_events`: the same event type resolution
  (aliases, the stored .NET type as fallback) and the same upcasters. `store.Events.DeserializeAsync(ColdEvent)`
  exposes this for events held anywhere else.

Inline and async aggregations read their stored snapshot, which already contains the cold part, and do not
read the cold event store.

## Knowing that an append continues a cold stream

`await session.Events.ColdVersionOfAsync(streamAction)` answers, for a stream the unit of work appends to,
its cold version, so an `IDocumentSessionListener` can update the application's own bookkeeping in the same
transaction, whether the events were appended after `FetchForWriting` or with a plain `Append`. The first
call looks up every appended stream the session has not fetched in one query; a session that never asks pays
nothing.

Every appended stream whose cold version the session observed, through this call or a live fetch, is checked
again at commit, behind the appends' own row locks. If another transaction marked it cold in between, the
commit fails with `ColdStreamChangedException` and nothing is written; retry the unit of work.
