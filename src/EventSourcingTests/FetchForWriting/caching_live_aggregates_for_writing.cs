using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
///     #5225: <c>CacheAggregatesForWriting&lt;T&gt;()</c> for an aggregate with no stored snapshot, built Live on
///     every fetch. The cache is only ever a baseline: the stream version and every event after the cached
///     version are read from the database on every fetch, so a stale or wrong entry costs a bigger read and never
///     a wrong aggregate. Before #5225 enrolling a Live aggregate did nothing at all.
/// </summary>
public class caching_live_aggregates_for_writing: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    private void UseCachedLiveAggregates(Action<StoreOptions> more = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
            more?.Invoke(opts);
        });
    }

    private async Task<Guid> startStream(params object[] events)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<SimpleAggregate>(streamId, events);
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task append(Guid streamId, params object[] events)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, events);
        await session.SaveChangesAsync();
    }

    private async Task warmUp(Guid streamId)
    {
        await using var session = theStore.LightweightSession();
        await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        // jasperfx#1050: nothing reaches the cache before the session commits
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task cold_fetch_matches_the_uncached_aggregate_and_seeds_the_cache()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new BEvent(), new BEvent(), new CEvent());

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(0);
        theCache.Misses.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(4);
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(2);
        stream.Aggregate.CCount.ShouldBe(1);
        stream.Aggregate.Id.ShouldBe(streamId);

        // Nothing is in the cache until the session commits...
        theCache.Keys.ShouldBeEmpty();
        await session.SaveChangesAsync();

        // ... and then the fetch has left its aggregate behind at the stream version
        var (aggregate, version) = theCache.SingleEntry();
        version.ShouldBe(4);
        aggregate.ShouldBeSameAs(stream.Aggregate);
    }

    [Fact]
    public async Task a_cache_hit_folds_only_the_events_after_the_cached_version()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());
        await warmUp(streamId);

        // Poison the cached baseline. A fetch that replays the whole stream comes back with 5; one that folds
        // only events 4 and 5 onto the baseline comes back with 102. Nothing else lands on 102.
        theCache.Overwrite(theCache.SingleKey(), new SimpleAggregate { Id = streamId, ACount = 100 }, 3);
        await append(streamId, new AEvent(), new AEvent());

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(5);
        stream.Aggregate.ACount.ShouldBe(102);

        await session.SaveChangesAsync();
        var (aggregate, version) = theCache.SingleEntry();
        version.ShouldBe(5);
        ((SimpleAggregate)aggregate).ACount.ShouldBe(102);
    }

    [Fact]
    public async Task an_exclusive_fetch_uses_the_cache_too()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());
        await warmUp(streamId);
        theCache.Overwrite(theCache.SingleKey(), new SimpleAggregate { Id = streamId, ACount = 100 }, 3);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(3);
        stream.Aggregate.ACount.ShouldBe(100);
        stream.AppendOne(new AEvent());
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task each_hop_of_a_chain_reads_only_the_previous_hops_events()
    {
        UseCachedLiveAggregates(opts => opts.OpenTelemetry.TrackFetchForWritingMetrics());
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent(), new AEvent(), new AEvent(),
            new AEvent(), new AEvent(), new AEvent(), new AEvent(), new AEvent());

        using var recorder = new EventsReplayedRecorder("marten.fetch_for_writing.events_replayed");

        // Six handlers, one stream, each in its own session and transaction, each appending two events
        for (var hop = 0; hop < 6; hop++)
        {
            await using var session = theStore.LightweightSession();
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.Aggregate.ACount.ShouldBe(10 + hop);
            stream.Aggregate.BCount.ShouldBe(hop);
            stream.CurrentVersion.ShouldBe(10 + 2 * hop);

            stream.AppendMany(new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        theCache.Hits.ShouldBe(5);

        // The first hop reads the whole stream, and every hop after it the two events the hop before appended
        lock (recorder.Measurements)
        {
            recorder.Measurements
                .Where(x => x.AggregateType == typeof(SimpleAggregate).FullNameInCode() && x.FetchPlan == "Live")
                .Select(x => x.Value)
                .ShouldBe([10, 2, 2, 2, 2, 2]);
        }
    }

    [Fact]
    public async Task a_stale_entry_still_yields_the_correct_aggregate()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());
        await warmUp(streamId);

        // The cache is now three versions behind
        await append(streamId, new BEvent(), new BEvent(), new CEvent());

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(6);
        stream.Aggregate.ACount.ShouldBe(3);
        stream.Aggregate.BCount.ShouldBe(2);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task optimistic_concurrency_still_fires_against_a_warm_cache()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());
        await warmUp(streamId);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        theCache.Hits.ShouldBe(1);
        stream.AppendOne(new EEvent());

        // Somebody else gets there first
        await append(streamId, new EEvent());

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync());

        // The failed commit wrote nothing back
        theCache.Keys.ShouldBeEmpty();
    }

    [Fact]
    public async Task a_cache_ahead_of_the_database_falls_back_to_the_uncached_path()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());
        await warmUp(streamId);

        // A database restored out from under a cache that had run ahead
        theCache.Overwrite(theCache.SingleKey(), new SimpleAggregate { Id = streamId, ACount = 100 }, 99);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        stream.CurrentVersion.ShouldBe(3);
        stream.Aggregate.ACount.ShouldBe(3);

        // and the cache heals on the same round, once the session commits
        await session.SaveChangesAsync();
        var (aggregate, version) = theCache.SingleEntry();
        version.ShouldBe(3);
        ((SimpleAggregate)aggregate).ACount.ShouldBe(3);
    }

    [Fact]
    public async Task an_archived_stream_is_not_served_from_the_cache()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());
        await warmUp(streamId);
        theCache.Keys.Count.ShouldBe(1);

        await using (var archive = theStore.LightweightSession())
        {
            archive.Events.ArchiveStream(streamId);
            await archive.SaveChangesAsync();
        }

        // Its events are left out of a Live read, so the uncached fetch finds no aggregate, and the cached one
        // must not bring it back
        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(3);
    }

    [Fact]
    public async Task a_tombstoned_stream_is_not_served_from_the_cache()
    {
        UseCachedLiveAggregates();
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());
        await warmUp(streamId);

        await using (var tombstone = theStore.LightweightSession())
        {
            tombstone.Events.TombstoneStream(streamId);
            await tombstone.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);
    }

    [Fact]
    public async Task folding_this_sessions_own_events_in_session_does_not_reach_the_cache()
    {
        UseCachedLiveAggregates(opts => opts.Events.UseIdentityMapForAggregates = true);
        var streamId = await startStream(new AEvent(), new AEvent(), new AEvent());

        await using (var session = theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.AppendOne(new AEvent());

            // With the identity map on, this folds the pending event onto the very instance the fetch registered
            // for the cache, which then holds an event its version (3) says it does not
            var latest = await session.Events.FetchLatest<SimpleAggregate>(streamId);
            latest.ACount.ShouldBe(4);

            await session.SaveChangesAsync();
        }

        await using var next = theStore.LightweightSession();
        var again = await next.Events.FetchForWriting<SimpleAggregate>(streamId);
        again.CurrentVersion.ShouldBe(4);
        again.Aggregate.ACount.ShouldBe(4);
    }

    [Fact]
    public async Task cached_aggregates_do_not_leak_across_tenants()
    {
        UseCachedLiveAggregates(opts => opts.Events.TenancyStyle = TenancyStyle.Conjoined);

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

        foreach (var tenant in new[] { "one", "two", "one", "two" })
        {
            await using var session = theStore.LightweightSession(tenant);
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.Aggregate.ACount.ShouldBe(tenant == "one" ? 3 : 0);
            stream.Aggregate.BCount.ShouldBe(tenant == "one" ? 0 : 2);
            await session.SaveChangesAsync();
        }

        theCache.Hits.ShouldBe(2);
        theCache.Keys.Select(x => x.TenantId).OrderBy(x => x).ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task caching_is_off_unless_the_aggregate_type_opts_in()
    {
        StoreOptions(opts => opts.Events.AggregateWriteCaching.Cache = theCache);
        var streamId = await startStream(new AEvent(), new AEvent());

        await using var session = theStore.LightweightSession();
        await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        await session.SaveChangesAsync();
        await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(0);
        theCache.Misses.ShouldBe(0);
        theCache.Keys.ShouldBeEmpty();
    }
}
