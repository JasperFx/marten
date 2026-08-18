using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events.Fetching;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
///     Covers the opt in aggregate snapshot cache for FetchForWriting against <b>Inline</b> lifecycle
///     projections, which behave differently from Async in two load bearing ways:
///     <list type="number">
///         <item>An Inline snapshot is written in the same transaction as the events, so it is always
///         exactly at the stream head. There is no delta query to reconcile a stale entry with, so a cached
///         entry is usable only on an exact version match and anything else falls back to the database.</item>
///         <item>The inline projection applies the caller's appended events to the very instance
///         FetchForWriting handed out, during the commit. So the entry may only be written back <i>after</i>
///         a successful commit -- writing it at fetch time would leave the cache describing state that is
///         durable only if SaveChangesAsync happens to succeed.</item>
///     </list>
/// </summary>
public class caching_inline_aggregates_for_writing: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    private void UseCachedInlineSnapshots()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
        });
    }

    [Fact]
    public async Task cold_fetch_misses_and_seeds_the_cache_only_after_the_commit()
    {
        UseCachedInlineSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(0);
        theCache.Misses.ShouldBe(1);

        // Nothing cached yet -- the commit has not happened, so nothing is known to be durable
        theCache.Keys.ShouldBeEmpty();

        stream.AppendOne(new CEvent());
        await session.SaveChangesAsync();

        // ...and now it is, at the version the commit actually landed on
        theCache.SingleEntry().Version.ShouldBe(4);
    }

    [Fact]
    public async Task a_cache_hit_returns_the_same_aggregate_as_the_uncached_path()
    {
        UseCachedInlineSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(),
            new CEvent());
        await theSession.SaveChangesAsync();

        // Round one seeds the cache
        // Note the append: a session that fetches and then commits *nothing* has no commit to hook, so
        // the write back only happens on rounds that actually change the stream.
        await using (var warmUp = theStore.LightweightSession())
        {
            var warmUpStream = await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
            warmUpStream.AppendOne(new AEvent());
            await warmUp.SaveChangesAsync();
        }

        theCache.Keys.Count.ShouldBe(1);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);

        stream.CurrentVersion.ShouldBe(5);

        var expected = await session.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        stream.Aggregate.ACount.ShouldBe(expected.ACount);
        stream.Aggregate.BCount.ShouldBe(expected.BCount);
        stream.Aggregate.CCount.ShouldBe(expected.CCount);
    }

    [Fact]
    public async Task a_stale_entry_falls_back_to_the_database_rather_than_returning_it()
    {
        UseCachedInlineSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        // Seed the cache at version 2
        // Note the append: a session that fetches and then commits *nothing* has no commit to hook, so
        // the write back only happens on rounds that actually change the stream.
        await using (var warmUp = theStore.LightweightSession())
        {
            var warmUpStream = await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
            warmUpStream.AppendOne(new AEvent());
            await warmUp.SaveChangesAsync();
        }

        // Something else moves the stream past the cached entry. This session does not go through
        // FetchForWriting, so the cache is left behind at version 2 while the database is at 5.
        await using (var other = theStore.LightweightSession())
        {
            other.Events.Append(streamId, new CEvent(), new CEvent(), new CEvent());
            await other.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        // The stale entry must not be handed back -- an Inline snapshot has no delta fold to repair it
        stream.CurrentVersion.ShouldBe(6);

        var expected = await session.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        stream.Aggregate.ACount.ShouldBe(expected.ACount);
        stream.Aggregate.BCount.ShouldBe(expected.BCount);
        stream.Aggregate.CCount.ShouldBe(expected.CCount);
    }

    [Fact]
    public async Task a_failed_commit_leaves_no_entry_behind()
    {
        UseCachedInlineSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        // Seed the cache at version 2
        // Note the append: a session that fetches and then commits *nothing* has no commit to hook, so
        // the write back only happens on rounds that actually change the stream.
        await using (var warmUp = theStore.LightweightSession())
        {
            var warmUpStream = await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
            warmUpStream.AppendOne(new AEvent());
            await warmUp.SaveChangesAsync();
        }

        theCache.SingleEntry().Version.ShouldBe(3);

        // Fetch (taking the entry out of the cache), mutate through an append, then fail the commit by
        // racing another writer to the same stream so the OCC assertion trips.
        await using (var doomed = theStore.LightweightSession())
        {
            var stream = await doomed.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.AppendOne(new CEvent());

            await using (var racer = theStore.LightweightSession())
            {
                racer.Events.Append(streamId, new CEvent());
                await racer.SaveChangesAsync();
            }

            await Should.ThrowAsync<Exception>(async () => await doomed.SaveChangesAsync());
        }

        // The decisive assertion: take-on-read removed the entry at fetch time and the failed commit never
        // wrote one back, so the cache is empty rather than holding an aggregate that was mutated past what
        // the database durably has.
        theCache.Keys.ShouldBeEmpty();

        // ...and the next fetch is therefore correct
        await using var session = theStore.LightweightSession();
        var recovered = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        var expected = await session.Events.AggregateStreamAsync<SimpleAggregate>(streamId);

        recovered.Aggregate.ACount.ShouldBe(expected.ACount);
        recovered.Aggregate.BCount.ShouldBe(expected.BCount);
        recovered.Aggregate.CCount.ShouldBe(expected.CCount);
    }

    [Fact]
    public async Task repeated_fetch_write_rounds_stay_correct_across_many_hits()
    {
        UseCachedInlineSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        for (var i = 0; i < 12; i++)
        {
            await using var session = theStore.LightweightSession();
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.AppendOne(new BEvent());
            stream.AppendOne(new CEvent());
            await session.SaveChangesAsync();
        }

        await using var verify = theStore.LightweightSession();
        var actual = await verify.LoadAsync<SimpleAggregate>(streamId);
        var expected = await verify.Events.AggregateStreamAsync<SimpleAggregate>(streamId);

        actual.ACount.ShouldBe(expected.ACount);
        actual.BCount.ShouldBe(expected.BCount);
        actual.CCount.ShouldBe(expected.CCount);
        actual.BCount.ShouldBe(12);
        actual.CCount.ShouldBe(12);

        // Every round after the first took its snapshot from the cache
        theCache.Hits.ShouldBe(11);
    }
}
