using System;
using System.Threading.Tasks;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public readonly record struct StrongTypedOrderSummaryId(Guid Value);

/// <summary><see cref="OrderSummary" /> with a strong-typed id.</summary>
public class StrongTypedOrderSummary
{
    public StrongTypedOrderSummaryId Id { get; set; }

    [MappedNaturalKey<OrderAggregate>]
    public OrderNumber Number { get; set; }

    public int Items { get; set; }

    public static StrongTypedOrderSummary Create(OrderCreated e) => new() { Number = e.OrderNumber };

    public void Apply(OrderItemAdded e) => Items++;
}

/// <summary>
/// Fetches by natural key of an Async aggregate that <see cref="async_fetch_reads_in_a_safe_order_by_natural_key" />
/// can't express: a fetch repeated in one session, a snapshot the caller already loaded, and a write-cache hit with
/// an append between its reads. On an aggregate with a Guid id and one with a strong-typed id.
/// </summary>
public class async_fetch_by_natural_key_edge_cases: OneOffConfigurationsContext
{
    public enum Fetch
    {
        ForWriting,
        Latest
    }

    private void configure(bool strongTyped, bool cache = false)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Events.UseIdentityMapForAggregates = false;

            if (strongTyped)
            {
                opts.RegisterValueType<StrongTypedOrderSummaryId>();
                opts.Projections.Snapshot<StrongTypedOrderSummary>(SnapshotLifecycle.Async);
            }
            else
            {
                opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
                if (cache) opts.Events.CacheAggregatesForWriting<OrderSummary>();
            }
        });
    }

    private static string snapshotTable(bool strongTyped) =>
        strongTyped ? "mt_doc_strongtypedordersummary" : "mt_doc_ordersummary";

    private async Task catchTheDaemonUpAsync()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.CatchUpAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> startOrderAsync(OrderNumber number, int items)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<OrderAggregate>(streamId, new OrderCreated(number, "Alice"));
        for (var i = 0; i < items; i++) session.Events.Append(streamId, new OrderItemAdded("Widget", 1m));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return streamId;
    }

    private async Task addAnItemAsync(Guid streamId)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, new OrderItemAdded("Gadget", 1m));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The items the fetched aggregate counted, and the stream version for a fetch for writing.</summary>
    private static async Task<(int? items, long? version)> fetchAsync(IDocumentSession session, bool strongTyped,
        Fetch fetch, OrderNumber number)
    {
        var events = session.Events;
        var ct = TestContext.Current.CancellationToken;

        if (strongTyped)
        {
            if (fetch == Fetch.Latest) return ((await events.FetchLatest<StrongTypedOrderSummary, OrderNumber>(number, ct))?.Items, null);

            var stream = await events.FetchForWriting<StrongTypedOrderSummary, OrderNumber>(number, ct);
            return (stream.Aggregate?.Items, stream.CurrentVersion);
        }

        if (fetch == Fetch.Latest) return ((await events.FetchLatest<OrderSummary, OrderNumber>(number, ct))?.Items, null);

        var summary = await events.FetchForWriting<OrderSummary, OrderNumber>(number, ct);
        return (summary.Aggregate?.Items, summary.CurrentVersion);
    }

    private static void shouldBe((int? items, long? version) found, int items, long version)
    {
        found.items.ShouldBe(items);
        if (found.version.HasValue) found.version.Value.ShouldBe(version);
    }

    public static TheoryData<bool, Fetch> EveryAggregateAndFetch() => new()
    {
        { false, Fetch.ForWriting }, { false, Fetch.Latest }, { true, Fetch.ForWriting }, { true, Fetch.Latest }
    };

    [Theory]
    [MemberData(nameof(EveryAggregateAndFetch))]
    public async Task an_async_aggregate_fetched_twice_in_one_session_is_folded_once(bool strongTyped, Fetch fetch)
    {
        configure(strongTyped);
        var number = new OrderNumber($"TWICE-{strongTyped}-{fetch}");
        var streamId = await startOrderAsync(number, 1);
        await catchTheDaemonUpAsync();
        await addAnItemAsync(streamId);

        await using var session = theStore.LightweightSession();
        shouldBe(await fetchAsync(session, strongTyped, fetch, number), 2, 3);
        shouldBe(await fetchAsync(session, strongTyped, fetch, number), 2, 3);
    }

    [Fact]
    public async Task a_fetch_by_natural_key_leaves_a_snapshot_the_caller_loaded_alone()
    {
        configure(strongTyped: false);
        var number = new OrderNumber("LOADED");
        var streamId = await startOrderAsync(number, 1);
        await catchTheDaemonUpAsync();
        await addAnItemAsync(streamId);

        // Through the identity map, where the fetch could reach it
        await using var session = theStore.IdentitySession();
        var loaded = await session.LoadAsync<OrderSummary>(streamId, TestContext.Current.CancellationToken);
        loaded!.Items.ShouldBe(1);

        (await session.Events.FetchLatest<OrderSummary, OrderNumber>(number, TestContext.Current.CancellationToken))!
            .Items.ShouldBe(2);
        loaded.Items.ShouldBe(1);
    }

    // A write-cache hit by key reads the stream version through the key and then the events after the cached
    // version. An append between the two is in the events, so the fetch is at the newer version, and is cached
    // at it.

    [Fact]
    public async Task a_write_cache_hit_answers_at_an_event_appended_between_its_reads()
    {
        configure(strongTyped: false, cache: true);
        var number = new OrderNumber("CACHED");
        var streamId = await startOrderAsync(number, 1);

        // Fills the cache
        await using (var warming = theStore.LightweightSession())
        {
            shouldBe(await fetchAsync(warming, false, Fetch.ForWriting, number), 1, 2);
        }

        await using (var session = theStore.LightweightSession())
        {
            var pause = new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(() => addAnItemAsync(streamId),
                snapshotTable(false), streamVersionRead: true);
            session.Logger = pause;

            shouldBe(await fetchAsync(session, false, Fetch.ForWriting, number), 2, 3);
            await pause.Paused;
        }

        // The cached aggregate is labelled with the version it is at, so the item is not counted again
        await using var next = theStore.LightweightSession();
        shouldBe(await fetchAsync(next, false, Fetch.ForWriting, number), 2, 3);
    }
}
