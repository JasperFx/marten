# Backfilling a New Member Without a Version Bump

Adding a member to a projected document usually means a [projection version bump](/events/projections/rebuilding#blue-green-deployments-with-projection-versioning):
a new table, and a replay of every event the projection reads from the start of the store. When the new member comes
from one or two event types, almost all of that replay recomputes members that are already correct.

A **backfill** fills the new member on the documents that already exist, from only the events that produce it,
while the projection keeps running. The projection keeps its version, its table and its progression.

<!-- snippet: sample_declaring_a_projection_backfill -->
<a id='snippet-sample_declaring_a_projection_backfill'></a>
```cs
public class OrderSummaryProjection: SingleStreamProjection<OrderSummary, Guid>
{
    public OrderSummaryProjection()
    {
        // No version bump: the documents that already exist get Year and LineCount from this backfill
        Backfill("year-and-line-count")
            .On<OrderPlaced>((patch, e) => patch.Set(x => x.Year, e.Date.Year))
            .On<OrderLineAdded>((patch, _) => patch.Increment(x => x.LineCount));
    }

    // From here on the projection maintains both members itself
    public OrderSummary Create(IEvent<OrderPlaced> e) =>
        new() { Id = e.StreamId, Customer = e.Data.Customer, Year = e.Data.Date.Year };

    public void Apply(OrderSummary summary, OrderLineAdded e)
    {
        summary.Total += e.Amount;
        summary.LineCount++;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/Backfills/ProjectionBackfillSamples.cs#L23-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_declaring_a_projection_backfill' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Backfill(name)` is available on `SingleStreamProjection` and `MultiStreamProjection`, including as members of a
[composite projection](/events/projections/composite). Each `On<TEvent>` handler receives the
[Patch API](/documents/partial-updates-patching) for the document the projection would route that event to, and
`OnEvent<TEvent>` hands it the `IEvent<TEvent>` when the patch needs event metadata.

## How a backfill runs

The backfill runs inside the projection's own [async daemon](/events/projections/async-daemon) agent, next to the
live projection, the first time the agent starts on the new code:

1. **It owns the member until the hand-over.** It folds its event types from the start of the store, following the
   live projection's progression, into a side table (`mt_doc_{document}_backfill`) that the live projection never
   writes. Because one writer applies every event exactly once and in order, `Increment` and other folds are as
   safe as `Set`.
2. **It reads only what it needs.** It loads only its own event types (plus the projection's delete events) through
   the daemon's event loader, so the `(type, seq_id)` index and per-tenant partition pruning apply, and it finds
   the documents through the projection's own slicer, so `Identity<T>`, fan-out and custom grouping land on exactly
   the documents the projection wrote.
3. **It merges as it goes.** After each page it merges what it changed into the documents, so most documents carry
   the new member long before the backfill completes.
4. **It hands over.** Once it is within one batch of the live projection, the agent holds its loading until nothing
   is in flight, the backfill folds the last events, re-merges every document the live projection rewrote since its
   merge, and records itself as completed. From then on the projection's own `Apply` owns the member. The hold
   lasts for that final step only.

The backfill only ever writes documents that **still exist and are not soft-deleted**: a merge updates existing rows,
so a deleted document is never brought back, and a projection delete event starts that document's backfilled
member over. Duplicated fields over a backfilled member are kept in step.

Progress is recorded per shard in `mt_projection_backfills`, so a restart resumes where it stopped, and a completed
backfill never runs again. A projection that starts from nothing, or a full rebuild, records its backfills as
completed without doing any work, because the new `Apply` produces the member for every event anyway.

Per-tenant shards (`UseTenantPartitionedEvents`) each run their own backfill, so they work unchanged with
Wolverine's managed event subscription distribution: whichever node starts a tenant's shard runs its backfill, and
a node that takes over resumes from the recorded position. A forced catch-up (`daemon.CatchUpAsync()`,
`ForceAllMartenDaemonActivityToCatchUpAsync()`) runs a shard's backfills before catching it up.

## What a backfill costs

A version bump replays every event the projection reads (for a composite, every member's events) into new tables.
A backfill reads only its own event types and writes only the documents they touch, in pages of the shard's batch
size (for a composite member, the composite's):

- **Reading** is proportional to the backfill's events, not the store's. A single event type is served by the
  `(type, seq_id)` index (`opts.Events.EnableEventTypeIndex`); a few common types by the sequence index.
- **Writing** costs one side-table upsert and one merge per document per page it appears in. The merge finds its rows
  through a partial index, so a page costs what it changed, not the size of the backfill.
- **The hand-over** makes one pass over the backfilled documents of the shard to re-merge what the live projection
  rewrote, while the shard is held. That pass grows with the number of documents, not events.

The gain is therefore largest when the new member comes from a small share of the events, and for composites, where a
bump would rebuild every member. When the backfill's event types are most of what the projection reads, it does
about the same work as a rebuild, but without a version, a new table or a pause in projecting.

## Waiting for and watching a backfill

Until a backfill is handed over, the member it fills is incomplete, even though the projection's progression is
current. The non-stale waits therefore treat a projection as current only once **every backfill of every shard has
completed**: `WaitForNonStaleProjectionDataAsync()`, `IProjectionDaemon.WaitForNonStaleData()`, the per-document-type
wait behind `QueryForNonStaleData`, and the read-only wait of an externally managed daemon. A timeout names the
backfills that had not completed.

To see where each backfill stands, per shard:

```cs
var backfills = await store.Advanced.AllProjectionBackfills();
foreach (var backfill in backfills)
{
    // e.g. "OrderSummary:All:year-and-line-count", or "OrderSummary:All:tenant-a:year-and-line-count" per tenant
    Console.WriteLine($"{backfill.Name}: {backfill.Position}/{backfill.Ceiling}, completed: {backfill.IsCompleted}");
}
```

A running daemon also publishes it: while a shard owes backfills, every `ShardState` it publishes (including its
heartbeat) carries `Backfills`, with each backfill's position, the live position it is following, and a `Failure`
when it stopped. An `IObserver<ShardState>` on `daemon.Tracker`, or a monitoring tool subscribed to it, sees the
progress without polling. The shard's own sequence and mode stay those of the live projection, so its lag remains
honest while a backfill runs.

## Rules for a backfill

- **The handlers must agree with `Apply`.** The backfill computes the member for the events before the hand-over;
  the projection's own `Apply` produces it for every event after.
- **A handler sees one event, not the document.** It starts from an empty object and folds the events of its own
  types in order. A member derived from *other* members of the document, or from `Updated<T>` in a later composite
  stage, cannot be backfilled this way.
- **Only aggregations backfill.** `SingleStreamProjection` and `MultiStreamProjection` own the identity of their
  documents; an `EventProjection` or flat table projection writes whatever it likes, so there is nothing to fold onto.
- **Only async projections run backfills.** Declaring one on an inline projection is a configuration error.
- **Register the projection as a singleton.** A scoped or transient registration resolves the projection per batch,
  so there is no single projection to hand the member over; Marten refuses it at startup.
- **Renaming a backfill runs it again**; once every shard has completed it, the declaration can be removed.
- **Old code must not run the shard after the hand-over.** A node still on the previous code writes documents without
  the new member. Make sure old nodes no longer pick up projection agents during a rolling deployment.
