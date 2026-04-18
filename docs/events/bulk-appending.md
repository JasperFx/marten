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
