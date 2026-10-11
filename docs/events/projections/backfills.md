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
a node that takes over resumes from the recorded position.

## Rules for a backfill

- **The handlers must agree with `Apply`.** The backfill computes the member for the events before the hand-over;
  the projection's own `Apply` produces it for every event after.
- **A handler sees one event, not the document.** It starts from an empty object and folds the events of its own
  types in order. A member derived from *other* members of the document, or from `Updated<T>` in a later composite
  stage, cannot be backfilled this way.
- **Only async projections run backfills.** Declaring one on an inline projection is a configuration error.
- **Renaming a backfill runs it again**; once every shard has completed it, the declaration can be removed.
- **Until it is handed over, the member is incomplete.** Waiting for non-stale data waits for the projection's
  progression, not for its backfills; read `mt_projection_backfills` to see where a backfill stands.
- **Old code must not run the shard after the hand-over.** A node still on the previous code writes documents without
  the new member. Make sure old nodes no longer pick up projection agents during a rolling deployment.
