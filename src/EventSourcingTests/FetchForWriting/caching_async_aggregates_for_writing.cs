using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using JasperFx.Events.Fetching;
using Marten.Events.Fetching;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
///     Covers the opt in aggregate snapshot cache for FetchForWriting against Async lifecycle projections.
///     The load bearing claim under test is that the cache is only ever a *baseline*: the stream version and
///     the events after the cached version are always read from the database, so a stale or even wrong entry
///     degrades to a bigger delta query rather than a wrong aggregate.
/// </summary>
public class caching_async_aggregates_for_writing: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    private void UseCachedAsyncSnapshots()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
        });
    }

    [Fact]
    public async Task cold_fetch_matches_the_uncached_aggregate_and_seeds_the_cache()
    {
        UseCachedAsyncSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(),
            new BEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(0);
        theCache.Misses.ShouldBe(1);

        stream.CurrentVersion.ShouldBe(6);

        // Cross check against an independent live replay of the same stream
        var expected = await session.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        stream.Aggregate.ACount.ShouldBe(expected.ACount);
        stream.Aggregate.BCount.ShouldBe(expected.BCount);
        stream.Aggregate.CCount.ShouldBe(expected.CCount);
        stream.Aggregate.Id.ShouldBe(streamId);

        // ... and the fetch left a snapshot behind at the stream version
        var (_, version) = theCache.SingleEntry();
        version.ShouldBe(6);
    }

    [Fact]
    public async Task cache_hit_folds_only_the_delta()
    {
        UseCachedAsyncSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        // Seed the cache
        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        // Deliberately poison the cached baseline. If the next fetch replays the whole stream, ACount comes
        // back 5; if it folds only events 4 and 5 onto the baseline we handed it, it comes back 102. There
        // is no other way to land on 102.
        var key = theCache.SingleKey();
        theCache.Overwrite(key, new SimpleAggregate { Id = streamId, ACount = 100 }, 3);

        await using (var other = theStore.LightweightSession())
        {
            other.Events.Append(streamId, new AEvent(), new AEvent());
            await other.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(5);
        stream.Aggregate.ACount.ShouldBe(102);

        // and the poisoned-but-now-folded snapshot is written back at the real stream version
        var (aggregate, version) = theCache.SingleEntry();
        version.ShouldBe(5);
        ((SimpleAggregate)aggregate).ACount.ShouldBe(102);
    }

    [Fact]
    public async Task stale_cache_entry_still_yields_the_correct_aggregate()
    {
        UseCachedAsyncSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        // The cache is now three versions behind
        await using (var other = theStore.LightweightSession())
        {
            other.Events.Append(streamId, new BEvent(), new BEvent(), new CEvent());
            await other.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(6);

        var expected = await session.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        stream.Aggregate.ACount.ShouldBe(expected.ACount);
        stream.Aggregate.BCount.ShouldBe(expected.BCount);
        stream.Aggregate.CCount.ShouldBe(expected.CCount);
    }

    [Fact]
    public async Task optimistic_concurrency_still_fires_against_a_warm_cache()
    {
        UseCachedAsyncSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(3);
        stream.AppendOne(new EEvent());

        // Somebody else gets there first
        await using (var other = theStore.LightweightSession())
        {
            other.Events.Append(streamId, new EEvent());
            await other.SaveChangesAsync();
        }

        // The safety net is untouched by the cache
        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
        {
            await session.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task cache_ahead_of_the_database_falls_back_to_the_uncached_path()
    {
        UseCachedAsyncSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        // Pretend the database was restored out from under a cache that had run ahead
        var key = theCache.SingleKey();
        theCache.Overwrite(key, new SimpleAggregate { Id = streamId, ACount = 100 }, 99);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        stream.CurrentVersion.ShouldBe(3);
        stream.Aggregate.ACount.ShouldBe(3);

        // and the cache healed itself on the same call rather than staying poisoned
        var (aggregate, version) = theCache.SingleEntry();
        version.ShouldBe(3);
        ((SimpleAggregate)aggregate).ACount.ShouldBe(3);
    }

    [Fact]
    public async Task cached_aggregates_do_not_leak_across_tenants()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async).MultiTenanted();
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
        });

        // Same stream id, two tenants, deliberately different shapes
        var streamId = Guid.NewGuid();

        await using (var one = theStore.LightweightSession("one"))
        {
            one.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
            await one.SaveChangesAsync();
        }

        await using (var two = theStore.LightweightSession("two"))
        {
            two.Events.StartStream<SimpleAggregate>(streamId, new BEvent(), new BEvent());
            await two.SaveChangesAsync();
        }

        // Cold fetches to seed both tenants
        await using (var one = theStore.LightweightSession("one"))
        {
            var stream = await one.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.Aggregate.ACount.ShouldBe(3);
            stream.Aggregate.BCount.ShouldBe(0);
        }

        await using (var two = theStore.LightweightSession("two"))
        {
            var stream = await two.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.Aggregate.ACount.ShouldBe(0);
            stream.Aggregate.BCount.ShouldBe(2);
        }

        theCache.Keys.Count.ShouldBe(2);
        theCache.Keys.Select(x => x.TenantId).OrderBy(x => x).ShouldBe(new[] { "one", "two" });

        // Warm fetches must stay in their lane
        await using (var one = theStore.LightweightSession("one"))
        {
            var stream = await one.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.Aggregate.ACount.ShouldBe(3);
            stream.Aggregate.BCount.ShouldBe(0);
        }

        await using (var two = theStore.LightweightSession("two"))
        {
            var stream = await two.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.Aggregate.ACount.ShouldBe(0);
            stream.Aggregate.BCount.ShouldBe(2);
        }

        theCache.Hits.ShouldBe(2);
    }

    [Fact]
    public async Task caching_is_off_unless_the_aggregate_type_opts_in()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            // deliberately NOT calling CacheAggregatesForWriting<SimpleAggregate>()
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(0);
        theCache.Misses.ShouldBe(0);
        theCache.Keys.ShouldBeEmpty();
    }

    [Fact]
    public void take_is_exclusive_so_two_readers_cannot_share_one_mutable_instance()
    {
        var cache = new RecentlyUsedAggregateWriteCache(10);
        var key = new AggregateCacheKey(typeof(SimpleAggregate), "db", "*DEFAULT*", Guid.NewGuid());
        cache.Store(key, new SimpleAggregate(), 4);

        cache.TryTake(key, out var first, out var firstVersion).ShouldBeTrue();
        first.ShouldNotBeNull();
        firstVersion.ShouldBe(4);

        // Take-on-read: the second caller misses and takes the uncached path rather than folding delta
        // events onto the same instance the first caller is holding.
        cache.TryTake(key, out _, out _).ShouldBeFalse();
    }
}

/// <summary>
///     Test double with the same take-on-read contract as the real cache, but with the guts exposed so a
///     test can seed a deliberately stale or wrong entry.
/// </summary>
public class MartenTestAggregateWriteCache: IAggregateWriteCache
{
    private readonly ConcurrentDictionary<AggregateCacheKey, (object Aggregate, long Version)> _entries = new();

    public long Hits { get; private set; }
    public long Misses { get; private set; }

    public IReadOnlyList<AggregateCacheKey> Keys => _entries.Keys.ToList();

    public bool TryTake(AggregateCacheKey key, [NotNullWhen(true)] out object? aggregate, out long version)
    {
        if (_entries.TryRemove(key, out var entry))
        {
            aggregate = entry.Aggregate;
            version = entry.Version;
            Hits++;
            return true;
        }

        aggregate = default;
        version = 0;
        Misses++;
        return false;
    }

    public void Store(AggregateCacheKey key, object aggregate, long version)
    {
        _entries[key] = (aggregate, version);
    }

    public void Evict(AggregateCacheKey key)
    {
        _entries.TryRemove(key, out _);
    }

    public AggregateCacheKey SingleKey()
    {
        return _entries.Keys.Single();
    }

    public (object Aggregate, long Version) SingleEntry()
    {
        return _entries.Values.Single();
    }

    public void Overwrite(AggregateCacheKey key, object aggregate, long version)
    {
        _entries[key] = (aggregate, version);
    }
}
