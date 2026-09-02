using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
///     Covers the opt in aggregate snapshot cache for FetchForWriting against Live aggregates — the
///     lifecycle the cache was proposed for, because it is the one with no stored snapshot to start from.
///     Uncached, every fetch replays the whole stream; cached, the entry is the baseline and only the events
///     after it are read.
/// </summary>
/// <remarks>
///     The load bearing claim is the same as for the other two lifecycles: the entry is only ever a
///     baseline. The stream version and every event after the cached version still come from the database,
///     so a stale or outright wrong entry degrades to a bigger delta query rather than a wrong aggregate.
/// </remarks>
public class caching_live_aggregates_for_writing: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    private void UseCachedLiveAggregates()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<SimpleAggregate>();
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
        });
    }

    [Fact]
    public async Task cold_fetch_matches_the_uncached_aggregate_and_seeds_the_cache()
    {
        UseCachedLiveAggregates();

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
        UseCachedLiveAggregates();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        // Seed the cache
        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        // Deliberately poison the cached baseline. If the next fetch replays the whole stream — which is
        // exactly what the uncached Live plan does — ACount comes back 5. If it folds only events 4 and 5
        // onto the baseline we handed it, it comes back 102. There is no other way to land on 102.
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
    public async Task cache_hit_with_no_new_events_returns_the_baseline_unchanged()
    {
        UseCachedLiveAggregates();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        // The empty-delta case is the one a chain of cascading commands over one stream actually hits, so
        // it needs to be right rather than merely fast.
        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(3);
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_latest_in_the_same_session_does_not_poison_the_entry()
    {
        UseCachedLiveAggregates();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        // Seed the cache
        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        // FetchForWriting -> append -> FetchLatest is the aggregate handler workflow's shape. Its item map
        // optimization folds the appended events onto the very instance the fetch handed out, and for an
        // aggregate that applies events in place -- which SimpleAggregate does, like most class aggregates
        // -- that instance is the one an entry stored at fetch time would still be pointing at. Left alone,
        // the entry would then claim a version below the state it actually holds, and the next fetch would
        // fold the same events onto it a second time.
        await using (var writer = theStore.LightweightSession())
        {
            var stream = await writer.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.AppendOne(new AEvent());

            var latest = await writer.Events.FetchLatest<SimpleAggregate>(streamId);
            latest.ACount.ShouldBe(4);

            await writer.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var reread = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        var expected = await session.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        reread.CurrentVersion.ShouldBe(4);
        reread.Aggregate.ACount.ShouldBe(expected.ACount);
    }

    [Fact]
    public async Task stale_cache_entry_still_yields_the_correct_aggregate()
    {
        UseCachedLiveAggregates();

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
        UseCachedLiveAggregates();

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

        // The safety net is untouched by the cache: the version handed to AppendToStream came from the
        // database, never from the entry.
        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
        {
            await session.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task cache_ahead_of_the_database_falls_back_to_the_uncached_path()
    {
        UseCachedLiveAggregates();

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
    public async Task an_archived_stream_is_not_resurrected_from_the_cache()
    {
        UseCachedLiveAggregates();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await using (var warmUp = theStore.LightweightSession())
        {
            await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
        }

        // Archiving is the one case where a Live delta of zero events does not mean "nothing changed": the
        // events are filtered out, so an uncached fetch answers null where the entry still holds the
        // aggregate. Without the is_archived check the cache would resurrect it.
        await using (var archiver = theStore.LightweightSession())
        {
            archiver.Events.ArchiveStream(streamId);
            await archiver.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.Aggregate.ShouldBeNull();

        // The uncached retry found nothing to cache, and take-on-read already dropped the stale entry
        theCache.Keys.ShouldBeEmpty();
    }

    [Fact]
    public async Task cached_aggregates_do_not_leak_across_tenants()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.LiveStreamAggregation<SimpleAggregate>();
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
            opts.Projections.LiveStreamAggregation<SimpleAggregate>();
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
}
