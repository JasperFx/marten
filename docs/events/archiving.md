# Archiving Event Streams

Like most (all?) event stores, Marten is designed around the idea of the events being persisted to a single file, immutable
log of events. All the same though, there are going to be problem domains where certain event streams become obsolete. Maybe
because a workflow is completed, maybe through time based expiry rules, or maybe because a customer or user is removed
from the system. To help optimize Marten's event store usage, you can take advantage of the stream archiving to 
mark events as archived on a stream by stream basis. 

::: warning Archiving is not erasure
Archiving marks or moves events, it does not remove them, and the data is still in the database. If you need to
erase a person's data, for example for a GDPR request, see [Personal data and crypto-shredding with Tayra](/events/protection#personal-data-and-crypto-shredding-with-tayra).
:::

::: tip
To compare archiving with stream compacting, see [Shrinking mt_events](/events/shrinking).
:::

::: warning
You can obviously use pure SQL to modify the events persisted by Marten. While that might be valuable in some cases,
we urge you to be cautious about doing so.
:::

The impact of archiving an event stream is:

* With the table partitioning model for "hot/cold" storage described below (the default since Marten 10), the stream and
  event rows are moved to the archived partition tables for streams and events
* Without partitioning (the default before Marten 10, and under per-tenant event partitioning), the relevant stream and
  event rows are marked with an `is_archived = TRUE`
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L36-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_archive_stream_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As in all cases with an `IDocumentSession`, you need to call `SaveChanges()` to commit the
unit of work.

::: tip
Archiving keeps the data. To delete a stream and its events for good, see
[Deleting a Stream Permanently](#deleting-a-stream-permanently).
:::

Archiving the same stream again preserves one archived copy of its stream and event rows,
including when hot/cold partitioning is enabled. A single-stream projection with
`IncludeArchivedEvents = true` can process the `Archived` marker after an inline projection
has already archived the stream.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L237-L244' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_for_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also query for all events both archived and not archived with `MaybeArchived()`
like so:

<!-- snippet: sample_query_for_maybe_archived_events -->
<a id='snippet-sample_query_for_maybe_archived_events'></a>
```cs
var events = await theSession.Events.QueryAllRawEvents()
    .Where(x => x.MaybeArchived()).ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L270-L275' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_maybe_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Appending to an Archived Stream

An archived stream is closed to new events. Appending to one fails the transaction with
`JasperFx.Events.ArchivedStreamException`, whose `Id` is the stream's id or key — the same type Polecat and Fisher throw
(before Marten 10 this was the generic `Marten.Exceptions.InvalidStreamOperationException`). In Rich append mode the stream state is read before the
append and the check happens in Marten (`RichEventAppender`), and in Quick append mode the `mt_quick_append_events` function
raises SQLSTATE `MT001` which Marten translates into the same exception. Either way you get the same exception, so this
does not change with `EventAppendMode`.

To reopen an archived stream, un-archive it first. See [Un-archiving a Stream](#un-archiving-a-stream) below.

Note that `StartStream` against an archived stream's id is a **different** failure. The id is still in use, so it is an
identity collision (`ExistingStreamIdCollisionException`), not an archived-stream error. Under
`UseArchivedStreamPartitioning` the archived row lives in a different partition, and it is
`EnableStrictStreamIdentityEnforcement` (also on by default since Marten 10) that still refuses the reuse; turn it off
and the reuse is silently allowed. See
[Strict Stream Identity After Archive](/events/archiving.html#strict-stream-identity-after-archive) below.

## Un-archiving a Stream

`UnArchiveStream()` reverses `ArchiveStream()`. The stream and all of its events become live again: they come back in
`FetchStreamAsync()`, live aggregation, `FetchForWriting()`, raw event queries and tag queries, and the stream
accepts appends again.

<!-- snippet: sample_unarchive_stream -->
<a id='snippet-sample_unarchive_stream'></a>
```cs
await using var session = theStore.LightweightSession();

// Queue the un-archive. Like ArchiveStream, nothing touches the
// database until SaveChangesAsync
session.Events.UnArchiveStream(streamId);

// The string-keyed overload is used when StreamIdentity is AsString:
// session.Events.UnArchiveStream("customer-123");

await session.SaveChangesAsync();

// The stream and its events are live again, and can be appended to
session.Events.Append(streamId, new LedgerOpened("appended after un-archiving"));
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Archiving/unarchiving_streams.cs#L850-L867' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_unarchive_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`UnArchiveStream()` has a `Guid` overload and a `string` overload, matching your `StreamIdentity`. Calling the
overload that does not match the store's configured identity throws an `InvalidOperationException` right away.
Both are declared on the shared `JasperFx.Events.IEventStoreOperations`, so code written against that interface
(for Marten, Polecat or Fisher) can un-archive a stream too.

Some details worth knowing:

* **It is queued.** Nothing happens until you call `SaveChangesAsync()`, and the un-archive commits or rolls back
  with the rest of the unit of work.
* **Un-archive in its own save.** Save the un-archive before you append to the stream. Appending in the same
  `SaveChangesAsync()` as the un-archive is not supported: in Rich append mode the append is still refused with
  `ArchivedStreamException`.
* **The version does not change.** The stream keeps the version it had when it was archived, so an append with an
  expected version, or through `FetchForWriting()`, works as it did before the archive.
* **It is safe to repeat.** Un-archiving a stream that is not archived does nothing. Un-archiving a stream that does
  not exist is not an error and creates nothing. A stream can be archived and un-archived any number of times.
* **Only the named stream changes.** Under conjoined tenancy, only the stream in the session's tenant is un-archived.
  The same stream id in another tenant stays archived.
* **Hot/cold partitioning.** Under `UseArchivedStreamPartitioning`, the stream and event rows move from the archived
  partitions back to the active ones. The DCB tag rows and natural key rows move with them, so tag queries and
  natural key lookups find the stream again. If another stream has claimed the natural key while this one was
  archived, the other stream keeps it.
* **A reused identity is refused.** Under `UseArchivedStreamPartitioning` without
  [strict stream identity](/events/archiving.html#strict-stream-identity-after-archive), a new stream can be started with an archived
  stream's id. Two live streams cannot share an id, so un-archiving the old stream then fails on
  `SaveChangesAsync()` with an `InvalidStreamOperationException`, and nothing is changed. Archive or delete the new
  stream first.
* **Projections are not rewound.** `ArchiveStream()` appends no event, so projections never see the archive or the
  un-archive. An inline or async snapshot stays as it was and picks up new events as usual. The restored events keep
  their sequence numbers, which the async daemon has already passed, so they are not processed a second time. If the
  stream was archived by an [`Archived` event](/events/archiving.html#archived-event), that event is still in the stream after un-archiving,
  and any document your projection deleted or changed when it handled that event stays as it is. Rebuild the
  projection if it should reflect the reopened stream.

## Hot/Cold Storage Partitioning <Badge type="tip" text="7.25" />

Marten uses PostgreSQL native table partitioning on the event and event stream data to keep the "hot" (active) and
"cold" (archived) events in separate partition tables. This is on by default since Marten 10, and was opt in before.
It keeps the active `mt_streams` and `mt_events` tables smaller, which pretty well always results in better
performance over time if you archive streams. If you never archive anything, it costs very little.

It is off under [per-tenant event partitioning](/events/multitenancy.html#per-tenant-event-partitioning), which
partitions the same tables by tenant instead. To turn it off yourself, set the flag to `false` or call
`opts.RestoreV9Defaults()`:

<!-- snippet: sample_turn_on_stream_archival_partitioning -->
<a id='snippet-sample_turn_on_stream_archival_partitioning'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // PostgreSQL table partitioning for hot/cold storage
    // on archived events. On by default since Marten 10
    opts.Events.UseArchivedStreamPartitioning = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/Optimizations.cs#L14-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_turn_on_stream_archival_partitioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
If you are turning this option on (or off) for an existing system, including by upgrading to Marten 10 with the default,
you may want to run the database schema migration script
by hand rather than trying to let Marten do it automatically. The data migration from non-partitioned to partitioned
will probably require system downtime because it actually has to copy the old table data, drop the old table, create the new 
table, copy all the existing data from the temp table to the new partitioned table, and finally drop the temporary table.
:::

### Strict Stream Identity After Archive <Badge type="tip" text="8.x" />

::: tip
This setting is on by default since Marten 10, along with
`UseArchivedStreamPartitioning`, and off under per-tenant event partitioning. It matters most when stream identity is
generated **outside** of Marten — most commonly when the user is providing string keys themselves (order numbers,
external reference ids, etc.). With Marten-generated `Guid` ids, accidental reuse is vanishingly unlikely, so
turning it off to save the extra insert per new stream is reasonable.
:::

When `UseArchivedStreamPartitioning = true` is enabled, archiving a stream
physically moves its row from the active partition (`mt_streams_default`) to
the archived partition (`mt_streams_archived`). PostgreSQL requires the
partition key (`is_archived`) to be part of the primary key on a partitioned
table, so the effective stream-table PK becomes `(id, is_archived)`. That
means a fresh `StartStream` call with the same id as a previously-archived
stream **does not collide** — you can silently reuse the identity, ending up
with two rows: one active, one archived.

In the "classic" (non-partitioned) mode, archive merely flips
`is_archived = TRUE` on the existing row. The unique constraint on the id
still fires, so reuse throws `ExistingStreamIdCollisionException`. Without
extra configuration, the partitioning mode is therefore **less strict** about
identity reuse than the non-partitioned mode.

The `EnableStrictStreamIdentityEnforcement` flag gives you the strict behavior under both modes. These are the
Marten 10 defaults, written out:

```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // Recommended companion when stream ids come from outside Marten
    // (especially string keys) and you also want hot/cold partitioning.
    opts.Events.UseArchivedStreamPartitioning = true;
    opts.Events.EnableStrictStreamIdentityEnforcement = true;
});
```

Under the hood Marten creates a sibling, **non-partitioned**
`mt_streams_identity` table whose primary key is just the stream identity
(`(id)`, or `(tenant_id, id)` under conjoined tenancy). Each `StartStream` is
rewritten to also INSERT into that table in the same prepared statement (via
a modifying CTE), and so is an append that creates a new stream in the Quick
append modes, so a duplicate identity raises a unique violation that
Marten translates into the same `ExistingStreamIdCollisionException` you'd
expect in non-partitioned mode. Archive does **not** touch
`mt_streams_identity`, so the identity row stays put and the protection
spans the active / archived divide. When the table is first created, Marten
fills it from `mt_streams`, so streams from before the flag was turned on are
covered too.

The flag is cheap — it adds a second tiny INSERT per stream creation — and the
practical reason for it is exactly the combination above. If you turn
partitioning off, the `mt_streams` primary key already enforces this and the
flag is unnecessary.

## Archived Event <Badge type="tip" text="7.34" />

::: tip
The `Archived` type moved into the shared JasperFx.Events library for Marten 8.0.
:::

Marten has a built in event named `Archived` that can be appended to any event stream:

```cs
namespace JasperFx.Events;

/// <summary>
/// The presence of this event marks a stream as "archived" when it is processed
/// by a single stream projection of any sort
/// </summary>
public record Archived(string Reason);
```

When this event is appended to an event stream *and* that event is processed through any type of single stream projection
for that event stream (snapshot or what we used to call a "self-aggregate", `SingleStreamProjection`, or `CustomProjection` with the `AggregateByStream` option),
Marten will automatically mark that entire event stream as archived as part of processing the projection. This applies for
both `Inline` and `Async` execution of projections. 

Let's try to make this concrete by building a simple order processing system that might include this
aggregate:

<!-- snippet: sample_order_for_optimized_command_handling -->
<a id='snippet-sample_order_for_optimized_command_handling'></a>
```cs
public class Item
{
    public string Name { get; set; }
    public bool Ready { get; set; }
}

public class Order
{
    // This would be the stream id
    public Guid Id { get; set; }

    // This is important, by Marten convention this would
    // be the
    public long Version { get; set; }

    public Order(OrderCreated created)
    {
        foreach (var item in created.Items)
        {
            Items[item.Name] = item;
        }
    }

    public void Apply(IEvent<OrderShipped> shipped) => Shipped = shipped.Timestamp;
    public void Apply(ItemReady ready) => Items[ready.Name].Ready = true;

    public DateTimeOffset? Shipped { get; private set; }

    public Dictionary<string, Item> Items { get; set; } = new();

    public bool IsReadyToShip()
    {
        return Shipped == null && Items.Values.All(x => x.Ready);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L24-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order_for_optimized_command_handling' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Next, let's say we're having the `Order` aggregate snapshotted so that it's updated every time new events 
are captured like so:

<!-- snippet: sample_registering_order_as_inline -->
<a id='snippet-sample_registering_order_as_inline'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // The Order aggregate is updated Inline inside the
    // same transaction as the events being appended
    opts.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);

    // Opt into an optimization for the inline aggregates
    // used with FetchForWriting()
    opts.Projections.UseIdentityMapForAggregates = true;
})

// This is also a performance optimization in Marten to disable the
// identity map tracking overall in Marten sessions if you don't
// need that tracking at runtime
.UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L210-L231' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_order_as_inline' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, let's say as a way to keep our application performing as well as possible, we'd like to be aggressive about archiving
shipped orders to keep the "hot" event storage table small. One way we can do that is to append the `Archived` event 
as part of processing a command to ship an order like so:

<!-- snippet: sample_handling_shiporder_and_emitting_archived_event -->
<a id='snippet-sample_handling_shiporder_and_emitting_archived_event'></a>
```cs
public static async Task HandleAsync(ShipOrder command, IDocumentSession session)
{
    var stream = await session.Events.FetchForWriting<Order>(command.OrderId);
    var order = stream.Aggregate;

    if (!order.Shipped.HasValue)
    {
        // Mark it as shipped
        stream.AppendOne(new OrderShipped());

        // But also, the order is done, so let's mark it as archived too!
        stream.AppendOne(new Archived("Shipped"));

        await session.SaveChangesAsync();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L234-L253' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_handling_shiporder_and_emitting_archived_event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If an `Order` hasn't already shipped, one of the outcomes of that command handler executing is that the entire event stream
for the `Order` will be marked as archived. 

::: info
This was originally conceived as a way to improve the Wolverine aggregate handler workflow usability while also encouraging
Marten users to take advantage of the event archiving feature. 
:::

::: info
When an `Archived` event is appended to a stream (as described above), Marten will mark the entire event stream as archived once that event is processed.

As a result, asynchronous multi-stream projections will no longer process any other events from that stream, even if those events appear earlier in the sequence or were added in a different session and have not yet been processed.

If you rely on asynchronous multi-stream projections and need all events to be processed before archiving, you can work around this by either:

* Writing the `Archived` event as a side effect within your asynchronous projection, after all other events have been processed, or
* Delaying the archiving by publishing a scheduled or delayed message (for example, when using Marten together with Wolverine) that appends the `Archived` event after projection completion.

This ensures all prior events are fully projected before the stream is marked as archived.
:::

### Archived in Composite Projections <Badge type="tip" text="8.x" />

When multiple single-stream projections run inside the same
[composite projection](/events/projections/composite), every child projection
processes every slice in the batch. An `Archived` event raised on one child's
stream is therefore *seen* by every sibling. To avoid redundant stream-archival
operations, the `mt_archive_stream` call is only issued from the child that
actually **owns** the stream — measured by whether the child has a snapshot for
that stream id (either loaded before the slice was applied, or materialized by
the slice itself). Siblings that did not materialize anything skip the archive.

::: tip
Archiving a stream and deleting the projected document are **independent**
operations. Marten does not delete projected documents as a side effect of an
`Archived` event — that's a common and legitimate pattern: keep the read model
around for historical queries while the underlying event stream moves out of
the hot table. If you *do* want the document removed when the stream is
archived, either call `session.Delete<T>(id)` explicitly (see the earlier tip)
or register the deletion in your projection's `Apply(Archived, current)`
method by returning `null` (or otherwise opting in).
:::

Whether a projection's `Create(Archived)` or `Apply(Archived, current)` method
runs is entirely governed by the user-defined handlers on that projection —
`Archived` is just an event and receives no special treatment at the
`EvolveAsync` level. The ownership guard described above only scopes the
stream-archival side effect.

## Deleting a Stream Permanently

Archiving is a soft delete: the stream and its events stay in the database, just out of the way.
When the data really has to go, for example to purge a customer's records or to clean up after a
plan or workflow that should leave nothing behind, use `TombstoneStream()` instead. It is a **hard
delete** of the stream and every event in it:

<!-- snippet: sample_tombstone_stream -->
<a id='snippet-sample_tombstone_stream'></a>
```cs
await using var session = theStore.LightweightSession();

// Queue a hard delete of the stream, its events and their tag rows.
// Nothing touches the database until SaveChangesAsync.
session.Events.TombstoneStream(streamId);

// The string-keyed overload is used when StreamIdentity is AsString:
// session.Events.TombstoneStream("customer-123");

await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Archiving/tombstoning_streams.cs#L270-L283' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tombstone_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`TombstoneStream()` has a `Guid` overload and a `string` overload, matching your
`StreamIdentity`. Calling the overload that does not match the store's configured identity throws
an `InvalidOperationException` right away, the same guard the other stream operations use. The
method is part of the shared `JasperFx.Events.IEventStoreOperations` contract, so code written
against that abstraction works the same way on Marten, Polecat and Fisher.

What `TombstoneStream()` does, compared with `ArchiveStream()`:

| | `ArchiveStream()` | `TombstoneStream()` |
| --- | --- | --- |
| Stream row in `mt_streams` | kept, marked archived (or moved to the archived partition) | deleted |
| Event rows in `mt_events` | kept, marked archived | deleted |
| DCB tag rows (`mt_event_tag_*`) | kept | deleted |
| DCB tag versions (`mt_dcb_tag_version`) | kept | deleted when no other stream carries the tag |
| Can be undone | yes, with `UnArchiveStream()` | no |
| Stream id can start a new stream | depends on partitioning and strict identity settings | yes |
| Projected documents | left alone | left alone |

Some details worth knowing:

* **It is queued.** Like every other `IDocumentSession` operation, nothing happens until you call
  `SaveChangesAsync()`, and the delete commits or rolls back with the rest of the unit of work.
* **Tag rows go too.** For every registered DCB tag type, the stream's rows in that type's
  `mt_event_tag_*` table are deleted along with the events, so tag queries and
  `EventsExistAsync()` no longer find them. In `DcbStorageMode.HStore` the tags live on the event
  rows and leave with them. The tag's `mt_dcb_tag_version` row goes too once no other stream carries
  the tag, and a DCB boundary that read the stream's events can't save through the delete. See
  [what a tombstone erases](/events/dcb#what-a-tombstone-erases).
* **It is tenant scoped.** Under conjoined event tenancy the delete only reaches the session's
  tenant. Another tenant's stream with the same id is untouched.
* **A missing stream is a no-op.** Deleting a stream that does not exist, or that was already
  deleted, is not an error.
* **Every storage layout is covered.** The delete reaches the archived partition when
  `UseArchivedStreamPartitioning` is on, so archived streams can be deleted too, and the tenant's
  partition under `UseTenantPartitionedEvents`. Natural key rows are removed with the stream. With
  `EnableStrictStreamIdentityEnforcement`, the identity is released as well, so the same id can start
  a new stream afterwards.
* **Sequence numbers are not reused.** The event sequence is left alone. The deleted events'
  sequence numbers stay spent, so an async projection or subscription whose high water mark has
  already moved past them can never miss an event that later takes one of those numbers. Expect a
  gap in the sequence where the deleted events were.
* **Projected documents are left alone.** Nothing is "un-applied". Snapshots, single or multi
  stream projection documents and flat table rows built from the deleted events stay where they
  are. If they should go too, delete them in the same session with `session.Delete<T>(id)` or
  `session.DeleteWhere<T>()`.

::: warning
There is no way to recover a deleted stream. If you need to keep the data for audit or historical
queries, archive the stream instead.
:::

::: tip
In Marten's internals, "tombstone" also names the bookkeeping stream Marten uses to fill gaps in the
event sequence after a failed append. That has nothing to do with `TombstoneStream()`, which takes
its name from the shared `IEventStoreOperations` contract.
:::
