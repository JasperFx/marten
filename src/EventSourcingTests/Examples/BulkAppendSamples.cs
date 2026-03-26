using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;

namespace EventSourcingTests.Examples;

public static class BulkAppendSamples
{
    #region sample_bulk_append_events_basic

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

    #endregion

    #region sample_bulk_append_events_with_tenant

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

    #endregion

    #region sample_bulk_append_events_with_metadata

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

    #endregion

    #region sample_bulk_append_events_with_batch_size

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

    #endregion

    #region sample_bulk_append_events_string_identity

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

    #endregion
}

// Sample event types for documentation
public record BulkOrderPlaced(Guid OrderId, string Product, int Quantity);
public record BulkOrderShipped(Guid OrderId, string TrackingNumber);
public record BulkOrderDelivered(Guid OrderId, DateTimeOffset DeliveredAt);
