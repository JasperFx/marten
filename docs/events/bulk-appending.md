# Bulk Appending Events

<Badge type="tip" text="8.x" />

::: tip
This feature is intended for data seeding, migration from other event stores, load testing, and importing
events from external systems. For normal application event appending, use the standard
[Appending Events](/events/appending) API instead.
:::

Marten provides a high-throughput bulk event append API that uses PostgreSQL's `COPY ... FROM STDIN BINARY`
protocol to efficiently load large numbers of events into the event store. This bypasses the normal append
pipeline for maximum speed, making it suitable for scenarios where you need to insert millions or even
billions of events.

## How It Works

The bulk append API:

1. Pre-allocates event sequence numbers from the `mt_events_sequence`
2. Uses `NpgsqlBinaryImporter` to COPY stream records into `mt_streams`
3. Uses `NpgsqlBinaryImporter` to COPY event records into `mt_events`
4. Updates the high water mark in `mt_event_progression` so the async daemon knows where to start

This approach is significantly faster than the normal append path because it avoids per-row function
calls, version checking, and individual INSERT statements.

## Basic Usage

Build a list of `StreamAction` objects representing new event streams, then call `BulkInsertEventsAsync`
on the document store:

<!-- snippet: sample_bulk_append_events_basic -->
<a id='snippet-sample_bulk_append_events_basic'></a>
```cs
public static async Task BulkAppendBasicExample(DocumentStore store)
{
    // Build up a list of stream actions with events
    var streams = new List<StreamAction>();

    for (int i = 0; i < 1000; i++)
    {
        var streamId = Guid.NewGuid();
        var events = new object[]
        {
            new BulkOrderPlaced(streamId, "Widget", 5),
            new BulkOrderShipped(streamId, $"TRACK-{i}"),
            new BulkOrderDelivered(streamId, DateTimeOffset.UtcNow)
        };

        streams.Add(StreamAction.Start(store.Events, streamId, events));
    }

    // Bulk insert all events using PostgreSQL COPY for maximum throughput
    await store.BulkInsertEventsAsync(streams);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/BulkAppendSamples.cs#L12-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_append_events_basic' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Multi-Tenancy

When using [conjoined multi-tenancy](/events/multitenancy), use the tenant-specific overload:

<!-- snippet: sample_bulk_append_events_with_tenant -->
<a id='snippet-sample_bulk_append_events_with_tenant'></a>
```cs
public static async Task BulkAppendWithTenantExample(DocumentStore store)
{
    var streams = new List<StreamAction>();

    for (int i = 0; i < 500; i++)
    {
        var streamId = Guid.NewGuid();
        var events = new object[]
        {
            new BulkOrderPlaced(streamId, "Gadget", 2),
            new BulkOrderShipped(streamId, $"TRACK-{i}")
        };

        streams.Add(StreamAction.Start(store.Events, streamId, events));
    }

    // Bulk insert events for a specific tenant when using conjoined tenancy
    await store.BulkInsertEventsAsync("tenant-abc", streams);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/BulkAppendSamples.cs#L38-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_append_events_with_tenant' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Event Metadata

You can set metadata on individual events before bulk inserting. This works with any combination
of enabled metadata columns (correlation ID, causation ID, headers, user name):

<!-- snippet: sample_bulk_append_events_with_metadata -->
<a id='snippet-sample_bulk_append_events_with_metadata'></a>
```cs
public static async Task BulkAppendWithMetadataExample(DocumentStore store)
{
    var streamId = Guid.NewGuid();
    var events = new object[]
    {
        new BulkOrderPlaced(streamId, "Widget", 10),
        new BulkOrderShipped(streamId, "TRACK-123")
    };

    var action = StreamAction.Start(store.Events, streamId, events);

    // Set metadata on individual events before bulk inserting
    foreach (var e in action.Events)
    {
        e.CorrelationId = "import-batch-42";
        e.CausationId = "migration-job";
        e.SetHeader("source", "legacy-system");
    }

    await store.BulkInsertEventsAsync(new[] { action });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/BulkAppendSamples.cs#L62-L86' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_append_events_with_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Controlling Batch Size

For very large imports, you can control the COPY batch size. Each batch is a separate PostgreSQL
COPY operation, which helps manage memory usage:

<!-- snippet: sample_bulk_append_events_with_batch_size -->
<a id='snippet-sample_bulk_append_events_with_batch_size'></a>
```cs
public static async Task BulkAppendWithBatchSizeExample(DocumentStore store)
{
    var streams = new List<StreamAction>();

    // Generate a large number of streams
    for (int i = 0; i < 100_000; i++)
    {
        var streamId = Guid.NewGuid();
        streams.Add(StreamAction.Start(store.Events, streamId,
            new object[] { new BulkOrderPlaced(streamId, "Item", 1) }));
    }

    // Control the COPY batch size for memory management.
    // Each batch is a separate PostgreSQL COPY operation.
    await store.BulkInsertEventsAsync(streams, batchSize: 5000);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/BulkAppendSamples.cs#L88-L107' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_append_events_with_batch_size' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## String Stream Identity

The bulk append API works with both Guid and string stream identities:

<!-- snippet: sample_bulk_append_events_string_identity -->
<a id='snippet-sample_bulk_append_events_string_identity'></a>
```cs
public static async Task BulkAppendWithStringIdentityExample(DocumentStore store)
{
    // When using StreamIdentity.AsString, use string-keyed stream actions
    var streams = new List<StreamAction>();

    for (int i = 0; i < 100; i++)
    {
        var key = $"order-{Guid.NewGuid():N}";
        var events = new object[]
        {
            new BulkOrderPlaced(Guid.NewGuid(), "Widget", 1),
            new BulkOrderShipped(Guid.NewGuid(), $"TRACK-{i}")
        };

        streams.Add(StreamAction.Start(store.Events, key, events));
    }

    await store.BulkInsertEventsAsync(streams);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/BulkAppendSamples.cs#L109-L131' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_append_events_string_identity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Supported Configurations

The bulk append API supports all combinations of:

| Configuration | Options |
| ------------- | ------- |
| Stream identity | `AsGuid`, `AsString` |
| Tenancy | Single, Conjoined |
| Archived stream partitioning | On, Off |
| Metadata columns | Correlation ID, Causation ID, Headers, User Name (any combination) |

## Streaming Import of an Existing Event Log

`BulkInsertEventsAsync` materializes every event of the import in memory, which is fine for seeding but
not for migrating a large existing event store. For that scenario use the streaming overload,
`BulkInsertEventStreamAsync`, which consumes an `IAsyncEnumerable<IEvent>` lazily in `batchSize` blocks —
a tenant with millions of events imports in bounded memory:

```cs
// One small header per stream seeds mt_streams up front (the foreign-key target),
// while the events themselves stream through in COPY blocks.
var headers = new[]
{
    new BulkEventStreamHeader { Id = streamId, Version = 3, AggregateType = typeof(Order) }
};

await store.BulkInsertEventStreamAsync(
    tenantId,
    headers,
    ReadSourceEventsInSequenceOrderAsync(), // IAsyncEnumerable<IEvent>, in source seq_id order
    batchSize: 1000);
```

Three properties make this suitable as the primitive for event-store migrations:

- **Cross-stream ordering is preserved.** Each event receives the next ascending `seq_id` in arrival
  order, so supplying the source log's global order (read by the source's `seq_id`) reproduces the
  interleaving of events across streams — which multi-stream projections and subscriptions depend on.
  The batch overload assigns seq_ids the same way, honoring the order events carry in their `Sequence`.
- **Per-tenant sequences stay consistent.** Under per-tenant event partitioning, seq_ids are drawn
  from the tenant's own `mt_events_sequence_{suffix}` — the same sequence live appends use — so the first
  real append after a migration continues seamlessly instead of colliding with imported seq_ids.
- **One transaction per tenant.** A failed import rolls back cleanly, making per-tenant resume logic in a
  migration tool trivial.

The streaming overload deliberately performs **no schema work**: apply the schema at startup and register
the tenant (`AddMartenManagedTenantsAsync`, or `AddTenantToShardAsync` under sharded tenancy) before
importing. Schema application from inside a bulk-import loop costs DDL proportional to the number of
registered tenants for every fresh database it touches, which does not scale to migrations of hundreds of
tenants.

### Preserving Source Sequence Numbers

By default the streaming import assigns **new** `seq_id`s (drawn from the target's sequence, in arrival
order). When migrating between stores in the *same system* — most importantly the
[conjoined → per-tenant-partitioned migration](/events/multitenancy#migrating-an-existing-conjoined-store) —
renumbering history is exactly wrong: progression rows, downstream warehouses, audit logs, and any external
consumer that captured a sequence position would all be invalidated. For that case pass
`BulkEventSequenceMode.PreserveSourceSequence`:

```cs
await store.BulkInsertEventStreamAsync(
    tenantId,
    headers,
    ReadSourceEventsInSequenceOrderAsync(), // MUST be strictly ascending by the carried Sequence
    BulkEventSequenceMode.PreserveSourceSequence,
    batchSize: 1000);
```

In this mode every event keeps the `Sequence` it carries — per-tenant gaps are fine and expected, since a
conjoined source interleaves all tenants on one global sequence — and after the copy Marten:

- **Advances the target sequence past the imported maximum** with `setval` (the tenant's own
  `mt_events_sequence_{suffix}` under per-tenant partitioning, otherwise the store-global sequence), so the
  first live append can never re-issue an imported `seq_id`.
- **Seeds the tenant's `HighWaterMark:{tenantId}` progression row** at the imported maximum under
  per-tenant event partitioning. This matters: gaps *below* a persisted high-water mark are never
  revisited, so the daemon starts cleanly above the (gappy) imported history instead of gap-walking
  through it.

Events must arrive in strictly ascending `Sequence` order; anything else (including unnumbered events,
which arrive as `0`) is rejected before commit.

## Limitations

The bulk append API intentionally trades off features for throughput:

- **No inline projections** -- events are written directly without triggering inline projections. Use
  [async projections](/events/projections/async-daemon) and rebuild after bulk loading.
- **No optimistic concurrency** -- there is no version checking against existing streams. This API is
  designed for initial data loading, not concurrent writes.
- **New streams only** -- bulk append creates new streams. It does not support appending to existing streams.
- **No event tags** -- DCB tag operations are not included in the COPY pipeline. Tags would need to be
  handled separately after bulk loading.

## Performance

In local benchmarks, the bulk append API achieves approximately **80,000-110,000 events/second**
depending on event complexity and PostgreSQL configuration. This compares to approximately
**60,000-80,000 events/second** using Marten's QuickAppend mode with parallel sessions.

The bulk append approach is especially advantageous when loading tens of millions of events or more,
where the reduced per-event overhead of PostgreSQL COPY becomes significant.
