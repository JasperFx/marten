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

`IColdEventStore` also has `ReadManyAsync`, used when several streams are read at once (see
[Reading](#reading)). Its default calls `ReadAsync` once per stream; override it to read them all in one
query. It returns each event with its stream key; streams may interleave, but each stream's events come
oldest first, the same as from `ReadAsync`.

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

### Moving many streams at once

A move job usually handles hundreds of streams per transaction. `MarkStreamsCold` takes them all, each with
its own version, for the session's tenant:

```cs
session.Events.MarkStreamsCold([(firstKey, 5), (secondKey, 12), (thirdKey, 3)]);
await session.SaveChangesAsync();
```

The batch is one statement, and the async shards are checked once, against the highest sequence the batch
deletes. Per stream it behaves exactly like `MarkStreamCold`. The batch succeeds or fails as a whole: when one
stream does not exist or has not reached its version, nothing is deleted and the error lists those streams;
when an async shard is behind on any stream of the batch, nothing is deleted either. A stream listed twice
moves through the higher version.

Calls to `MarkStreamCold` one after the other in the same unit of work are combined into one such statement
as well, so a loop over streams costs the same as one `MarkStreamsCold`.

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
- The raw events of a stream, cold part included, are an explicit opt-in, because the cold store may be
  slower or costlier to read than `mt_events`:

```cs
using Marten.Events.Cold;

// One stream, with the same version and timestamp filters as FetchStreamAsync
var events = await session.Events.FetchStreamWithColdAsync(streamKey, version: 10);

// Many streams: one query on mt_events, one ReadManyAsync on the cold store
IReadOnlyDictionary<string, IReadOnlyList<IEvent>> streams =
    await session.Events.FetchStreamsWithColdAsync(streamKeys);
```

  `FetchStreamWithColdAsync` asks the cold store only for the part the filters can include, and not at all
  when `fromVersion` is above the cold part. `FetchStreamsWithColdAsync` returns each stream's whole history
  in version order, for the session's tenant, and leaves out keys that are not a stream.

## Single-stream projections of a cold stream

An inline or async single-stream projection keeps working on a cold stream as long as its stored document is
there: it reads the document and projects the new events onto it, and never calls the cold store.

You may also move the stream's projected documents out together with its events. When such a stream gets a
new event, the projection finds no stored document, and Marten starts it from the cold part instead of from
empty:

1. It asks the cold store for the document it kept, through `TryLoadSnapshotAsync<TDoc>`. Implement it when you
   keep the documents; it gets the cold version the document has to be as of.
2. When that returns null (the default), it folds the cold events through the projection, the same way a live
   fetch does.

```cs
public class MyColdEventStore: IColdEventStore
{
    // ReadAsync as above

    public async ValueTask<TDoc?> TryLoadSnapshotAsync<TDoc>(IQuerySession session, string streamKey,
        long coldVersion, CancellationToken token) where TDoc : notnull
    {
        // The document you moved out of the projection's table, or null to fold the cold events instead
        return default;
    }
}
```

This happens for every single-stream projection, with no opt-in, once a cold event store is configured: a
projection that folded only the new events would store a wrong document without any error. It only happens
when the stored document is missing, the stream has a cold version, and no warm event lies between the cold
part and the new events (one query per batch of missing documents; nothing at all without a cold store). A
warm event in between was projected already, so a document missing after it is the projection's own outcome,
for example a deletion, and is left alone. Multi-stream projections and rebuilds never start from the cold
part.

## What does not read the cold part

Only the live folds, the opt-in raw reads, and a single-stream projection starting a missing document, all
above, read the cold event store. Everything else sees `mt_events` alone. Where that would silently build
something wrong, Marten refuses instead:

| Operation | On a cold stream |
|---|---|
| Inline and async aggregations (snapshots) | Correct, also when you moved the stored document out together with the events: see [Single-stream projections of a cold stream](#single-stream-projections-of-a-cold-stream). |
| Rebuilding a projection | Refused with `ColdStreamReplayException` while any stream in that database (or, for a per-tenant rebuild, that tenant) is cold, before anything is torn down. Allowed for a projection that [ignores cold streams](#projections-that-ignore-cold-streams); it then replays `mt_events` only. |
| A projection registered after streams went cold | Its first start from the beginning is refused the same way: the shard is paused with `ColdStreamReplayException`. A projection that starts from the present or from a later sequence is not affected. |
| `CompactStreamAsync` | Refused with `InvalidStreamOperationException`: its `Compacted<T>` snapshot would leave the cold part out, and every later fold jumps to it. |
| `FetchStreamAsync`, `QueryAllRawEvents()` | Return the events in `mt_events` only. Use `FetchStreamWithColdAsync` or `FetchStreamsWithColdAsync` for the whole stream. |

The check for rebuilds and new projections is one `exists` query on `mt_streams` per database (or tenant)
when a projection replays from the beginning; stores without a cold event store skip it.

### Projections that ignore cold streams

Some projections do not need the cold part at all, for example a read model the application derives for
cold streams itself. Mark them, and they rebuild and start from `mt_events` alone:

```cs
using Marten.Events.Cold;

var projection = new OpenCartsProjection();
projection.Options.IgnoreColdStreams();
opts.Projections.Add(projection, ProjectionLifecycle.Async);

// A composite projection replays as a whole, so the opt-in goes on the composite itself
opts.Projections.CompositeProjectionFor("Carts", composite =>
{
    composite.Add<OpenCartsProjection>();
    composite.Options.IgnoreColdStreams();
});
```

On a composite, only the composite's own options count; marking one of its members does nothing.

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
