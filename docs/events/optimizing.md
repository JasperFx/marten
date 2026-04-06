# Optimizing for Performance and Scalability <Badge type="tip" text="7.25" />

::: tip
The asynchronous projection and subscription support can in some cases suffer some event "skipping" when transactions
that are appending transactions become slower than the `StoreOptions.Projections.StaleSequenceThreshold` (the default is only 3 seconds).

From initial testing, the `Quick` append mode seems to stop this problem altogether. This only seems to be an issue with 
very large data loads.
:::

Marten has several options to potentially increase the performance and scalability of a system that uses
the event sourcing functionality:

<!-- snippet: sample_turn_on_optimizations_for_event_sourcing -->
<a id='snippet-sample_turn_on_optimizations_for_event_sourcing'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // Turn on the PostgreSQL table partitioning for
    // hot/cold storage on archived events
    opts.Events.UseArchivedStreamPartitioning = true;

    // Use the *much* faster workflow for appending events
    // at the cost of *some* loss of metadata usage for
    // inline projections
    opts.Events.AppendMode = EventAppendMode.Quick;

    // Little more involved, but this can reduce the number
    // of database queries necessary to process projections
    // during CQRS command handling with certain workflows
    opts.Events.UseIdentityMapForAggregates = true;

    // Opts into a mode where Marten is able to rebuild single
    // stream projections faster by building one stream at a time
    // Does require new table migrations for Marten 7 users though
    opts.Events.UseOptimizedProjectionRebuilds = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/Optimizations.cs#L32-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_turn_on_optimizations_for_event_sourcing' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The archived stream option is further described in the section on [Hot/Cold Storage Partitioning](/events/archiving.html#hot-cold-storage-partitioning).

See the ["Rich" vs "Quick" Appends](/events/appending.html#rich-vs-quick-appends) section for more information about the
applicability and drawbacks of the "Quick" event appending.

See [Optimizing FetchForWriting with Inline Aggregates](/scenarios/command_handler_workflow.html#optimizing-fetchforwriting-with-inline-aggregates) for more information
about the `UseIdentityMapForInlineAggregates` option.

Lastly, check out [Optimized Projection Rebuilds](/events/projections/rebuilding.html#optimized-projection-rebuilds) for information about `UseOptimizedProjectionRebuilds`

## Caching for Asynchronous Projections

You may be able to wring out more throughput for aggregated projections (`SingleStreamProjection`, `MultiStreamProjection`, `CustomProjection`)
by opting into 2nd level caching of the aggregated projected documents during asynchronous projection building. You can
do that by setting a greater than zero value for `CacheLimitPerTenant` directly inside of the aforementioned projection types
like so:

<!-- snippet: sample_showing_fanout_rules -->
<a id='snippet-sample_showing_fanout_rules'></a>
```cs
public class DayProjection: MultiStreamProjection<Day, int>
{
    public DayProjection()
    {
        // Tell the projection how to group the events
        // by Day document
        Identity<IDayEvent>(x => x.Day);

        // This just lets the projection work independently
        // on each Movement child of the Travel event
        // as if it were its own event
        FanOut<Travel, Movement>(x => x.Movements);

        // You can also access Event data
        FanOut<Travel, Stop>(x => x.Data.Stops);

        Name = "Day";

        // Opt into 2nd level caching of up to 100
        // most recently encountered aggregates as a
        // performance optimization
        Options.CacheLimitPerTenant = 1000;

        // With large event stores of relatively small
        // event objects, moving this number up from the
        // default can greatly improve throughput and especially
        // improve projection rebuild times
        Options.BatchSize = 5000;
    }

    public void Apply(Day day, TripStarted e)
    {
        day.Started++;
    }

    public void Apply(Day day, TripEnded e)
    {
        day.Ended++;
    }

    public void Apply(Day day, Movement e)
    {
        switch (e.Direction)
        {
            case Direction.East:
                day.East += e.Distance;
                break;
            case Direction.North:
                day.North += e.Distance;
                break;
            case Direction.South:
                day.South += e.Distance;
                break;
            case Direction.West:
                day.West += e.Distance;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Apply(Day day, Stop e)
    {
        day.Stops++;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/Aggregations/multi_stream_projections.cs#L250-L320' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_showing_fanout_rules' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten is using a most recently used cache for the projected documents that are being built by an aggregation projection
so that updates from new events can be directly applied to the in memory documents instead of having to constantly
load those documents over and over again from the database as new events trickle in. This is of course much more effective
when your projection is constantly updating a relatively small number of different aggregates.

## Event Type Index for Projection Rebuilds <Badge type="tip" text="8.29" />

If you have projections that filter on a small subset of event types and your event store has
large volumes of other event types, projection rebuilds can become very slow. The daemon's query
scans through ranges of events sequentially, and when matching events are sparse, most of the
scan is wasted.

Enable the event type index to add a composite index on `(type, seq_id)`:

```cs
opts.Events.EnableEventTypeIndex = true;
```

This creates:
```sql
CREATE INDEX idx_mt_events_event_type_seq_id ON mt_events (type, seq_id);
```

The index allows PostgreSQL to jump directly to matching event types within a sequence range,
turning projection rebuilds from O(N) full scans into O(log N) index lookups.

::: warning
This index adds storage overhead and slightly increases write latency on every event append.
Only enable it if you experience slow projection rebuilds with type-filtered projections.
:::

Even without the index, the async daemon automatically adapts when event loading times out.
It will fall back to progressively simpler query strategies:

1. **Normal**: Standard range query with type filter
2. **Skip-ahead**: Find the MIN(seq_id) matching the type filter, then fetch from there
3. **Window-step**: Advance through the sequence in fixed 10,000-event windows

This adaptive behavior is automatic and requires no configuration.

### When to Enable the Event Type Index

Consider enabling `EnableEventTypeIndex` if you observe any of these symptoms:

* **Projection rebuilds time out** — especially for projections that use `IncludeType<T>()` or
  only handle a small subset of your total event types
* **New async projections take a long time to catch up** — when deployed against an existing
  event store with millions of events and the projection only cares about a few event types
* **Blue/green deployments are slow** — the new projection version needs to rebuild from scratch
  and the event type distribution is uneven

You generally do **not** need this index if:

* Your event store is small (under a few million events)
* Your projections consume most or all event types
* You only use inline projections (no async daemon)

### Diagnosing Slow Projection Rebuilds

When the adaptive event loader falls back to a slower strategy, it logs a warning:

```text
Event loading timed out with Normal strategy for range [X, Y].
Falling back to SkipAhead. Consider enabling opts.Events.EnableEventTypeIndex
for better performance.
```

If you see these messages in your logs, enable the event type index and the warnings
will stop — the index eliminates the need for the fallback strategies entirely.

### Tuning Batch Size

The default batch size for the async daemon is 500 events per fetch. If you are
experiencing timeouts during projection rebuilds **and** cannot add the event type index,
you can reduce the batch size as a workaround:

```cs
opts.Projections.Snapshot<MyAggregate>(SnapshotLifecycle.Async, asyncOptions =>
{
    asyncOptions.BatchSize = 100;
});
```

A smaller batch size means smaller sequence ranges per query, reducing the chance of
scanning through large stretches of non-matching events. The trade-off is more round
trips to the database.

## Keeping the Database Smaller

One great way to maintain performance over time as a system database grows is to simply keep a lid on how big the **active**
data set is in your Marten database. To that end, you have a pair of complementary tools:

* [Event Archiving](/events/archiving)
* [Stream Compacting](/events/compacting)

## Distributed Async Projections with Wolverine

By default, async projection and subscription processing is coordinated across your application cluster using
Marten's built-in "Hot/Cold" leader election. An alternative is to use Wolverine's more sophisticated agent
distribution to spread projection work across all nodes in your application cluster:

```cs
builder.Services.AddMarten(opts =>
{
    // your configuration...
})
.IntegrateWithWolverine(opts =>
{
    opts.UseWolverineManagedEventSubscriptionDistribution = true;
});
```

This eliminates single-node bottlenecks in multi-instance deployments by distributing projection shards
across available nodes rather than centralizing all processing on the elected leader. See the
[Wolverine integration documentation](https://wolverinefx.net/guide/durability/marten/event-sourcing.html)
for more details.

For more on this topic, see [Wolverine-managed distribution](https://jeremydmiller.com/2025/06/02/making-event-sourcing-with-marten-go-faster/).
