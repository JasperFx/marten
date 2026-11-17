#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.Cold;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public class reading_raw_events_with_the_cold_part: OneOffConfigurationsContext
{
    private InMemoryColdEventStore _cold = new();

    private void useStringStreams(InMemoryColdEventStore? cold = null)
    {
        if (cold != null) _cold = cold;
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
        });
    }

    private async Task<string> startCart(params string[] items)
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Cart>(key,
            new object[] { new CartOpened("anne") }.Concat(items.Select(x => new ItemAdded(x))).ToArray());
        await session.SaveChangesAsync();
        return key;
    }

    private static string[] describe(IEnumerable<IEvent> events) => events.Select(x => x.Data switch
    {
        CartOpened opened => $"{x.Version}: opened by {opened.Customer}",
        ItemAdded added => $"{x.Version}: {added.Name}",
        _ => $"{x.Version}: {x.Data}"
    }).ToArray();

    [Fact]
    public async Task one_stream_is_read_cold_part_first()
    {
        useStringStreams();
        var key = await startCart("apple", "pear", "plum");
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var session = theStore.LightweightSession();
        var events = await session.Events.FetchStreamWithColdAsync(key);

        describe(events).ShouldBe(["1: opened by anne", "2: apple", "3: pear", "4: plum"]);
        events.ShouldAllBe(x => x.StreamKey == key && x.TenantId == StorageConstants.DefaultTenantId);
        _cold.ReadThrough.ShouldBe([2L]);

        // The plain read stays warm only
        (await session.Events.FetchStreamAsync(key)).Select(x => x.Version).ShouldBe([3L, 4L]);
    }

    [Fact]
    public async Task the_version_filters_apply_to_both_parts_and_bound_the_cold_read()
    {
        useStringStreams();
        var key = await startCart("apple", "pear", "plum", "fig");
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 3);

        await using var session = theStore.LightweightSession();

        describe(await session.Events.FetchStreamWithColdAsync(key, version: 2)).ShouldBe(["1: opened by anne", "2: apple"]);
        describe(await session.Events.FetchStreamWithColdAsync(key, version: 4)).ShouldBe(["1: opened by anne", "2: apple", "3: pear", "4: plum"]);
        describe(await session.Events.FetchStreamWithColdAsync(key, fromVersion: 3)).ShouldBe(["3: pear", "4: plum", "5: fig"]);
        describe(await session.Events.FetchStreamWithColdAsync(key, version: 4, fromVersion: 2)).ShouldBe(["2: apple", "3: pear", "4: plum"]);
        _cold.ReadThrough.ShouldBe([2L, 3L, 3L, 3L]);

        // Starting above the cold part never asks the cold store
        describe(await session.Events.FetchStreamWithColdAsync(key, fromVersion: 4)).ShouldBe(["4: plum", "5: fig"]);
        _cold.Reads.ShouldBe(4);
    }

    [Fact]
    public async Task the_timestamp_filter_applies_to_the_cold_part()
    {
        useStringStreams();
        var key = await startCart("apple");
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(key, new ItemAdded("pear"));
            await session.SaveChangesAsync();
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 3);

        await using var query = theStore.LightweightSession();
        var all = await query.Events.FetchStreamWithColdAsync(key);
        var secondAppend = all.Single(x => x.Version == 3).Timestamp;

        describe(await query.Events.FetchStreamWithColdAsync(key, timestamp: secondAppend.AddTicks(-1)))
            .ShouldBe(["1: opened by anne", "2: apple"]);
        describe(await query.Events.FetchStreamWithColdAsync(key, timestamp: secondAppend))
            .ShouldBe(["1: opened by anne", "2: apple", "3: pear"]);
    }

    [Fact]
    public async Task a_stream_without_a_cold_part_reads_like_fetch_stream()
    {
        useStringStreams();
        var key = await startCart("apple");

        await using var session = theStore.LightweightSession();
        describe(await session.Events.FetchStreamWithColdAsync(key)).ShouldBe(["1: opened by anne", "2: apple"]);
        (await session.Events.FetchStreamWithColdAsync(Guid.NewGuid().ToString())).ShouldBeEmpty();
        _cold.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task many_streams_are_read_whole_with_one_cold_read_per_stream()
    {
        useStringStreams();
        var allCold = await startCart("apple");
        var partlyCold = await startCart("pear", "plum");
        var warmOnly = await startCart("fig");
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, allCold, 2);
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, partlyCold, 2);

        await using var session = theStore.LightweightSession();
        var streams = await session.Events.FetchStreamsWithColdAsync(
            [allCold, partlyCold, warmOnly, partlyCold, Guid.NewGuid().ToString()]);

        streams.Keys.OrderBy(x => x).ShouldBe(new[] { allCold, partlyCold, warmOnly }.OrderBy(x => x));
        describe(streams[allCold]).ShouldBe(["1: opened by anne", "2: apple"]);
        describe(streams[partlyCold]).ShouldBe(["1: opened by anne", "2: pear", "3: plum"]);
        describe(streams[warmOnly]).ShouldBe(["1: opened by anne", "2: fig"]);
        streams[partlyCold].ShouldAllBe(x => x.StreamKey == partlyCold);

        // The default ReadManyAsync asks once per cold stream, and never for a warm one
        _cold.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task a_cold_store_can_read_many_streams_in_one_call()
    {
        var batching = new BatchingColdEventStore();
        useStringStreams(batching);
        var first = await startCart("apple");
        var second = await startCart("pear", "plum");
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, first, 2);
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, second, 2);
        var before = _cold.Reads;

        await using var session = theStore.LightweightSession();
        var streams = await session.Events.FetchStreamsWithColdAsync([first, second]);

        describe(streams[first]).ShouldBe(["1: opened by anne", "2: apple"]);
        describe(streams[second]).ShouldBe(["1: opened by anne", "2: pear", "3: plum"]);
        batching.ManyReads.ShouldBe(1);
        batching.Asked.OrderBy(x => x.StreamKey).ShouldBe(
            new[] { new ColdStreamRead(first, 2), new ColdStreamRead(second, 2) }.OrderBy(x => x.StreamKey));
        _cold.Reads.ShouldBe(before);
    }

    [Fact]
    public async Task a_cold_store_returning_a_stream_it_was_not_asked_for_is_refused()
    {
        var batching = new BatchingColdEventStore { Stray = "somebody-else" };
        useStringStreams(batching);
        var key = await startCart("apple");
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var session = theStore.LightweightSession();
        var e = await Should.ThrowAsync<InvalidOperationException>(() => session.Events.FetchStreamsWithColdAsync([key]));
        e.Message.ShouldContain("somebody-else");
    }

    [Fact]
    public async Task reading_no_streams_reads_nothing()
    {
        useStringStreams();
        await using var session = theStore.LightweightSession();
        (await session.Events.FetchStreamsWithColdAsync([])).ShouldBeEmpty();
    }
}

/// <summary>
///     Reads all streams in one call, interleaving their events the way a single SQL query ordered by version
///     would.
/// </summary>
public class BatchingColdEventStore: InMemoryColdEventStore, IColdEventStore
{
    public int ManyReads;
    public readonly ConcurrentBag<ColdStreamRead> Asked = new();
    public string? Stray { get; init; }

    async IAsyncEnumerable<(string StreamKey, ColdEvent Event)> IColdEventStore.ReadManyAsync(IQuerySession session,
        IReadOnlyList<ColdStreamRead> streams, [EnumeratorCancellation] CancellationToken token)
    {
        Interlocked.Increment(ref ManyReads);
        foreach (var stream in streams) Asked.Add(stream);

        var all = new List<(string StreamKey, ColdEvent Event)>();
        foreach (var stream in streams)
        {
            foreach (var version in StoredVersions(session.TenantId, stream.StreamKey).Where(x => x <= stream.ThroughVersion))
            {
                all.Add((stream.StreamKey, Event(session.TenantId, stream.StreamKey, version)));
            }
        }

        if (Stray != null && all.Count > 0) all.Insert(0, (Stray, all[0].Event));

        foreach (var item in all.OrderBy(x => x.Event.Version))
        {
            await Task.Yield();
            yield return item;
        }
    }
}
