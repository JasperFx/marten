#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Explorer;

public record OrderItem(string Sku, int Quantity);
public record OrderPlaced(string CustomerName);
public record ItemAdded(OrderItem Item);
public record OrderShipped(DateTimeOffset At);

public class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
    public bool Shipped { get; set; }

    public void Apply(OrderPlaced e) => CustomerName = e.CustomerName;
    public void Apply(ItemAdded e) => Items.Add(e.Item);
    public void Apply(OrderShipped _) => Shipped = true;
}

// Strong-typed identifier for DCB tagging.
public record CustomerId(Guid Value);

public record CustomerNotified(string Subject);

[Collection("OneOffs")]
public class event_store_explorer_tests: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<OrderPlaced>();
            opts.Events.AddEventType<ItemAdded>();
            opts.Events.AddEventType<OrderShipped>();
            opts.Events.AddEventType<CustomerNotified>();

            opts.Events.RegisterTagType<CustomerId>("customer");

            opts.Projections.LiveStreamAggregation<Order>();
        });
    }

    [Fact]
    public async Task get_recent_streams_returns_streams_ordered_by_timestamp_descending()
    {
        ConfigureStore();

        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        theSession.Events.Append(older, new OrderPlaced("Alice"));
        await theSession.SaveChangesAsync();

        await Task.Delay(50);

        theSession.Events.Append(newer, new OrderPlaced("Bob"));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var summaries = await explorer.GetRecentStreamsAsync(10, CancellationToken.None);

        summaries.Count.ShouldBeGreaterThanOrEqualTo(2);
        var ordered = summaries.Take(2).ToList();
        ordered[0].LastUpdatedAt.ShouldBeGreaterThanOrEqualTo(ordered[1].LastUpdatedAt);
        ordered.ShouldContain(s => s.StreamId == newer.ToString());
        ordered.ShouldContain(s => s.StreamId == older.ToString());
    }

    [Fact]
    public async Task get_recent_streams_caps_at_safety_bound()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        // Asking for a million should return at most 1000 — no error.
        var summaries = await explorer.GetRecentStreamsAsync(1_000_000, CancellationToken.None);
        summaries.Count.ShouldBeLessThanOrEqualTo(1000);
    }

    [Fact]
    public async Task get_recent_streams_zero_returns_empty()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        var summaries = await explorer.GetRecentStreamsAsync(0, CancellationToken.None);
        summaries.ShouldBeEmpty();
    }

    [Fact]
    public async Task read_stream_returns_events_in_version_order_with_raw_json()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new OrderPlaced("Charlie"),
            new ItemAdded(new OrderItem("WIDGET", 2)),
            new OrderShipped(DateTimeOffset.UtcNow));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var collected = new List<EventRecord>();
        await foreach (var e in explorer.ReadStreamAsync(streamId.ToString(), CancellationToken.None))
        {
            collected.Add(e);
        }

        collected.Count.ShouldBe(3);
        collected[0].EventTypeName.ShouldBe("order_placed");
        collected[1].EventTypeName.ShouldBe("item_added");
        collected[2].EventTypeName.ShouldBe("order_shipped");

        // versions are 1-based and monotonic
        collected[0].StreamVersion.ShouldBe(1);
        collected[2].StreamVersion.ShouldBe(3);

        // raw JSON body is preserved
        collected[0].Data.GetProperty("CustomerName").GetString().ShouldBe("Charlie");
        collected[1].Data.GetProperty("Item").GetProperty("Sku").GetString().ShouldBe("WIDGET");
    }

    [Fact]
    public async Task get_stream_metadata_returns_null_when_stream_missing()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        var meta = await explorer.GetStreamMetadataAsync(Guid.NewGuid().ToString(), CancellationToken.None);
        meta.ShouldBeNull();
    }

    [Fact]
    public async Task get_stream_metadata_returns_full_record()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new OrderPlaced("Dana"), new ItemAdded(new OrderItem("X", 1)));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var meta = await explorer.GetStreamMetadataAsync(streamId.ToString(), CancellationToken.None);

        meta.ShouldNotBeNull();
        meta!.StreamId.ShouldBe(streamId.ToString());
        meta.Version.ShouldBe(2);
        meta.IsArchived.ShouldBeFalse();
        meta.CreatedAt.ShouldBeLessThanOrEqualTo(meta.LastUpdatedAt);
    }

    [Fact]
    public async Task query_by_tags_returns_empty_for_unknown_tag_value()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        var results = new List<EventRecord>();
        var tags = new Dictionary<string, string> { { "CustomerId", Guid.NewGuid().ToString() } };
        await foreach (var e in explorer.QueryByTagsAsync(tags, CancellationToken.None))
        {
            results.Add(e);
        }

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task query_by_tags_single_tag_returns_matching_events()
    {
        ConfigureStore();
        var customerId = new CustomerId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var notification = theSession.Events.BuildEvent(new CustomerNotified("welcome"));
        notification.WithTag(customerId);
        theSession.Events.Append(streamId, notification);
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var results = new List<EventRecord>();
        var tags = new Dictionary<string, string> { { "CustomerId", customerId.Value.ToString() } };
        await foreach (var e in explorer.QueryByTagsAsync(tags, CancellationToken.None))
        {
            results.Add(e);
        }

        results.Count.ShouldBe(1);
        results[0].EventTypeName.ShouldBe("customer_notified");
        results[0].Data.GetProperty("Subject").GetString().ShouldBe("welcome");
    }

    [Fact]
    public async Task query_by_tags_throws_for_unregistered_tag_name()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        var tags = new Dictionary<string, string> { { "NotARealTag", "x" } };
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in explorer.QueryByTagsAsync(tags, CancellationToken.None))
            {
                // drain
            }
        });
    }

    [Fact]
    public async Task rehydrate_at_version_returns_aggregate_at_that_version()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new OrderPlaced("Eve"),
            new ItemAdded(new OrderItem("A", 1)),
            new ItemAdded(new OrderItem("B", 2)));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var atV2 = await explorer.RehydrateAtVersionAsync<Order>(streamId, version: 2, CancellationToken.None);

        atV2.State.ShouldNotBeNull();
        atV2.State!.CustomerName.ShouldBe("Eve");
        atV2.State.Items.Count.ShouldBe(1);
        atV2.State.Items[0].Sku.ShouldBe("A");
        atV2.Version.ShouldBe(2);
    }

    [Fact]
    public async Task rehydrate_at_version_by_name_returns_json_state()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new OrderPlaced("Frank"), new ItemAdded(new OrderItem("X", 1)));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var result = await explorer.RehydrateAtVersionByNameAsync(nameof(Order), streamId, version: 2, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.State.GetProperty("CustomerName").GetString().ShouldBe("Frank");
        result.Version.ShouldBe(2);
    }

    [Fact]
    public async Task rehydrate_at_version_by_name_returns_null_for_unknown_aggregate()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        var result = await explorer.RehydrateAtVersionByNameAsync("DoesNotExist", Guid.NewGuid(), version: 1, CancellationToken.None);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task get_projection_statuses_lists_registered_projections()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        var statuses = await explorer.GetProjectionStatusesAsync(CancellationToken.None);

        statuses.ShouldNotBeNull();
        // Live aggregation typically registers per-projection metadata; the snapshot is non-empty
        // when ANY projection is configured. We only assert structural fields.
        foreach (var status in statuses)
        {
            status.ProjectionName.ShouldNotBeNull();
            status.Lifecycle.ShouldNotBeNull();
            foreach (var shard in status.Shards)
            {
                shard.ShardName.ShouldNotBeNull();
                shard.State.ShouldBe("Unknown");
                shard.EventStoreSequence.ShouldBeGreaterThanOrEqualTo(0);
            }
        }
    }

    [Fact]
    public async Task try_create_usage_populates_dcb_tag_types_and_registered_event_types()
    {
        ConfigureStore();
        // Force schema to exist so DescribeDatabasesAsync doesn't blow up.
        theSession.Events.Append(Guid.NewGuid(), new OrderPlaced("Gus"));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var usage = await explorer.TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
        usage!.DcbTagTypes.ShouldContain(t => t.Name == nameof(CustomerId));
        usage.RegisteredEventTypes.ShouldContain(e => e.Alias == "order_placed");
        usage.RegisteredEventTypes.ShouldContain(e => e.Alias == "item_added");
    }

    // #782 / jasperfx#503 — on a conjoined multi-tenant store the same stream id can live
    // under two tenants. The tenant-less read returns an ambiguous union of both; the
    // tenant-scoped overload must isolate each tenant's slice via a tenant_id predicate.
    private void ConfigureConjoinedStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AddEventType<OrderPlaced>();
            opts.Events.AddEventType<ItemAdded>();
            opts.Policies.AllDocumentsAreMultiTenanted();
        });
    }

    [Fact]
    public async Task read_stream_is_isolated_per_tenant_on_conjoined_store()
    {
        ConfigureConjoinedStore();

        // Same stream id under two tenants — the exact cross-tenant ambiguity #782 closes.
        var streamId = Guid.NewGuid();

        await using (var a = theStore.LightweightSession("tenant-a"))
        {
            a.Events.StartStream<Order>(streamId, new OrderPlaced("Alice"), new ItemAdded(new OrderItem("A-1", 1)));
            await a.SaveChangesAsync();
        }

        await using (var b = theStore.LightweightSession("tenant-b"))
        {
            b.Events.StartStream<Order>(streamId, new OrderPlaced("Bob"));
            await b.SaveChangesAsync();
        }

        var explorer = (IEventStore)theStore;

        var tenantA = new List<EventRecord>();
        await foreach (var e in explorer.ReadStreamAsync(streamId.ToString(), "tenant-a", CancellationToken.None))
            tenantA.Add(e);

        var tenantB = new List<EventRecord>();
        await foreach (var e in explorer.ReadStreamAsync(streamId.ToString(), "tenant-b", CancellationToken.None))
            tenantB.Add(e);

        tenantA.Count.ShouldBe(2);
        tenantA.ShouldAllBe(e => e.TenantId == "tenant-a");
        tenantB.Count.ShouldBe(1);
        tenantB.ShouldAllBe(e => e.TenantId == "tenant-b");

        // The tenant-less overload is unchanged: it still reads across every tenant, so it
        // sees the union — this is the ambiguity the tenant-scoped read exists to resolve.
        var union = new List<EventRecord>();
        await foreach (var e in explorer.ReadStreamAsync(streamId.ToString(), CancellationToken.None))
            union.Add(e);
        union.Count.ShouldBe(3);
    }

    [Fact]
    public async Task recent_streams_is_isolated_per_tenant_on_conjoined_store()
    {
        ConfigureConjoinedStore();

        var streamA = Guid.NewGuid();
        var streamB = Guid.NewGuid();

        await using (var a = theStore.LightweightSession("tenant-a"))
        {
            a.Events.StartStream<Order>(streamA, new OrderPlaced("Alice"));
            await a.SaveChangesAsync();
        }

        await using (var b = theStore.LightweightSession("tenant-b"))
        {
            b.Events.StartStream<Order>(streamB, new OrderPlaced("Bob"));
            await b.SaveChangesAsync();
        }

        var explorer = (IEventStore)theStore;

        var tenantA = await explorer.GetRecentStreamsAsync(50, "tenant-a", CancellationToken.None);
        tenantA.ShouldAllBe(s => s.TenantId == "tenant-a");
        tenantA.ShouldContain(s => s.StreamId == streamA.ToString());
        tenantA.ShouldNotContain(s => s.StreamId == streamB.ToString());

        var tenantB = await explorer.GetRecentStreamsAsync(50, "tenant-b", CancellationToken.None);
        tenantB.ShouldAllBe(s => s.TenantId == "tenant-b");
        tenantB.ShouldContain(s => s.StreamId == streamB.ToString());
        tenantB.ShouldNotContain(s => s.StreamId == streamA.ToString());
    }
}
