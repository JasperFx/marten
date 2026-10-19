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
as before. `MarkStreamWarm` clears the boundary again, after you put the events back.

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

`session.Events.ColdVersionOf(streamAction)` answers, for a stream the unit of work appends to, the cold
version as it was stored before the unit of work. Marten resolves it for every appended stream before
`IDocumentSessionListener.BeforeSaveChangesAsync` runs, whether the events were appended after
`FetchForWriting` or with a plain `Append`, so a listener can update the application's own bookkeeping in
the same transaction.
