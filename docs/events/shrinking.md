# Shrinking mt_events <Badge type="tip" text="10.0" />

Left alone, `mt_events` only grows. Marten has two ways to keep it in check, and they do very different things:

* [Archiving a stream](/events/archiving) keeps every event, but takes the stream out of the active data set. It can be
  undone.
* [Compacting a stream](/events/compacting) deletes the stream's older events and leaves one `Compacted<T>` event
  holding a snapshot of one aggregate type in their place. That is lossy by design. The history is gone unless you copied
  it somewhere else with an archiver.

A third option is planned for Marten 10.1: moving the older part of a stream into storage your application owns, where
Marten can still read it.

## Side by Side

| | `ArchiveStream()` | `CompactStreamAsync<T>()` |
| --- | --- | --- |
| Works on | the whole stream | the stream's events up to a version or timestamp |
| Event rows in `mt_events` | kept, moved to the archived partition, or marked `is_archived` without partitioning | the compacted events deleted, except the last one, which is replaced by `Compacted<T>` at its sequence and version |
| Stream row in `mt_streams` | kept, moved to the archived partition, or marked `is_archived` | kept, `version` unchanged, `compacted_version` set to the compacted version |
| DCB tag rows | kept, but tag queries skip archived events | deleted with the events |
| Disk space | not freed. Partitioning moves the rows out of the active partition, it doesn't remove them | the deleted rows' space is reused after PostgreSQL vacuums the table |
| Appending afterwards | refused with `ArchivedStreamException` until you un-archive | works as before |
| Undo | `UnArchiveStream()` | none. Only your archiver's copy has the old events |
| Projected documents | left alone | left alone |

## What Stays Queryable

**An archived stream** is still in the database, but the normal reads leave it out:

* `FetchStreamAsync()` returns no events, and `AggregateStreamAsync()` returns null.
* Raw event queries skip archived events unless you ask for them with `IsArchived` or `MaybeArchived()`. See
  [Archiving Event Streams](/events/archiving).
* DCB tag queries skip archived events.
* `FetchStreamStateAsync()` still finds the stream, with `IsArchived` set.
* Documents projected from the stream, inline or async, are still there to load.

**A compacted stream** reads like a shorter stream:

* `FetchStreamAsync()` returns the `Compacted<T>` event, then any events appended after it.
* Live aggregation of `T` starts from the snapshot, so `AggregateStreamAsync<T>()`, `FetchForWriting<T>()` and
  `FetchLatest<T>()` give the same answer as before.
* Any other aggregate or projection of the stream only sees the events after the snapshot, unless it handles
  `Compacted<T>` itself. The rest of the history is gone.
* `FetchStreamStateAsync()` reports the compacted version in `CompactedVersion`, next to the stream's `Version`.

## Async Projections and Subscriptions

**Archiving has no guard.** The async daemon skips archived events, and that includes events a projection or
subscription had not reached yet when the stream was archived. Those events are never processed. A projection or
subscription that should see them sets `IncludeArchivedEvents = true` (see
[subscriptions](/events/subscriptions)). Otherwise, archive a stream after the daemon has caught up with it, for example
by appending an [`Archived` event](/events/archiving#archived-event) from an async projection or from a delayed message.
`ArchiveStream()` itself appends no event, so projections never see the archive happen.

**Compacting refuses to run while it could lose an event.** `CompactStreamAsync()` throws an
`InvalidOperationException` and compacts nothing when:

* an async projection or subscription has not processed the events yet. One that never ran counts as behind. A
  single-stream projection of `T` itself is exempt, because it reads the snapshot.
* an open high water skip could still hold a late event of the stream. The daemon's late event recovery checks a
  skipped range for up to `LateEventDetectionWindow` (one hour by default), and compaction waits until it's done.

Compact again once the projections have caught up or the skip has closed. The details are in
[Stream Compacting](/events/compacting).

After compaction, rebuilding the single-stream projection of `T` starts from the snapshot. Rebuilding anything else
only has the events after the snapshot to work with.

## Archived Stream Partitioning

`UseArchivedStreamPartitioning` is on by default since Marten 10. With it, `mt_events` and `mt_streams` are each split
into an active partition and an archived one, and archiving a stream moves its rows from one to the other. The active
partitions and their indexes stay small, and the daemon and the normal reads only scan them. That is where archiving's
performance benefit comes from.

Without partitioning, which is the Marten 9 default and what you get under
[per-tenant event partitioning](/events/multitenancy#per-tenant-event-partitioning), archiving only sets `is_archived`
on the rows. `mt_events` stays the same size, and the reads filter archived rows out. See
[Hot/Cold Storage Partitioning](/events/archiving#hot-cold-storage-partitioning) for the setting and for migrating an
existing database.

Compaction reads the stream the way `FetchStreamAsync()` does, so it doesn't see archived events, with or without
partitioning. Compacting an archived stream does nothing. To use both on one stream, compact first and archive
afterwards:

<!-- snippet: sample_compact_then_archive_a_stream -->
<a id='snippet-sample_compact_then_archive_a_stream'></a>
```cs
await using var session = theStore.LightweightSession();

// Compact first. Compacting only reads the stream's live events, so it
// has to happen before the archive. The five events become one
// Compacted<Shipment> event at version 5
await session.Events.CompactStreamAsync<Shipment>(streamId);
await session.SaveChangesAsync();

// Then archive the stream. Its one remaining event moves out of the
// active event table, or is marked archived without partitioning
session.Events.ArchiveStream(streamId);
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Archiving/archiving_and_compacting_one_stream.cs#L63-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compact_then_archive_a_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Which One to Use

* **The stream is finished, and you may need its history again** for an audit, a support question, or a projection
  that reads archived events: archive it.
* **The stream stays open and keeps growing, and only the current state of one aggregate matters**: compact it. Pass
  an archiver if you want to keep a copy of the deleted events outside Marten.
* **Both**: compact a long-lived stream from time to time while it is active, and archive it once it is finished.
* **Several projections of the same stream need its history**: archive, or don't shrink it at all. Compaction only
  keeps a snapshot of `T`.
* **Async projections or subscriptions are often behind**: compaction will refuse until they catch up, and archiving
  will make them miss events unless they set `IncludeArchivedEvents`.

Neither one erases data. Archived events are still in the database, and a `Compacted<T>` snapshot still holds whatever
the old events put into it. To remove a stream for good, use [`TombstoneStream()`](/events/archiving#deleting-a-stream-permanently).
To erase a person's data, see [Personal data and crypto-shredding with Tayra](/events/protection#personal-data-and-crypto-shredding-with-tayra).
