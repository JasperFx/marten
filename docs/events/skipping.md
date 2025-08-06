# Marking Events as Skipped <Badge type="tip" text="8.6" />

What if your code happens to append an event that turns out to be completely erroneous (not necessarily because of your code) and you wish you could
retroactively have it removed from the event store? You _could_ go and delete the event record directly from the `mt_events`
table, but maybe you have regulatory requirements that no events can ever be deleted -- which is the real world case that
spawned this feature. You _could_ also try a compensating event that effectively reverses the impact of the earlier, now
invalid event, but that requires more work and foresight on your part. 

Instead, you can mark events in a Marten event store as "skipped" such that these events are left as is in the database,
but will no longer be applied:

1. In projections. You'd have to rebuild a projection that includes a skipped event to update the resulting projection though.
2. Subscriptions. If you rewind a subscription and replay it, the events marked as "skipped" are, well, skipped
3. `AggregateStreamAsync()` in all usages
4. `FetchLatest()` and `FetchForWriting()` usages, but again, you may have to rebuild a projection to take the skipped events out of the results

::: tip
Definitely check out [Rebuilding a Single Stream](/events/projections/rebuilding.html#rebuilding-a-single-stream) for part of the recipe for "healing" a system from bad events.
:::

To get started, you will first have to enable potential event skipping like this:

snippet: sample_enabling_event_skipping

That flag just enables the ability to mark events as _skipped_. As you'd imagine, that 
flag alters Marten behavior by:

1. Adds a new field called `is_skipped` to your `mt_events` table
2. Adds an additional filter on `is_skipped = FALSE` on many event store operations detailed above

To mark events as skipped, you can either use raw SQL against your `mt_events` table, or
this helper API:
