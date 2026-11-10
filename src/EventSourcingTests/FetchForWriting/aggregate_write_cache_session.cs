using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
///     marten#5732 / jasperfx#1050. FetchForWriting's write-back to the aggregate write cache goes through
///     JasperFx's AggregateWriteCacheSession: nothing reaches the cache before a commit this session owns,
///     an entry is labelled with the version its instance reflects, and an Inline commit writes back the
///     instance its projection produced rather than the one it fetched.
/// </summary>
public class aggregate_write_cache_session: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    [Fact]
    public async Task inline_write_back_is_the_projected_instance_when_apply_returns_a_new_one()
    {
        // Marten's listener used to relabel the FETCHED instance with the committed version. An Apply that
        // returns a new instance leaves the fetched one where it was, so the entry claimed an event it did
        // not contain and the next fetch, matching on the version, served it.
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<ImmutableTally>(SnapshotLifecycle.Inline);
            opts.Events.UseIdentityMapForAggregates = false;
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<ImmutableTally>();
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<ImmutableTally>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        await using (var first = theStore.LightweightSession())
        {
            var stream = await first.Events.FetchForWriting<ImmutableTally>(streamId);
            stream.Aggregate.Count.ShouldBe(0);
            stream.AppendOne(new BEvent());
            await first.SaveChangesAsync();
        }

        theCache.SingleEntry().Version.ShouldBe(2);

        await using var second = theStore.LightweightSession();
        var again = await second.Events.FetchForWriting<ImmutableTally>(streamId);

        theCache.Hits.ShouldBe(1);
        again.CurrentVersion.ShouldBe(2);
        again.Aggregate.Count.ShouldBe(1);
    }

    [Fact]
    public async Task async_fetch_puts_nothing_in_the_cache_before_the_commit()
    {
        // The Async plan used to store the aggregate at fetch time, where a second session could claim the
        // very instance the first was still deciding from
        UseCachedAsyncSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        await using var first = theStore.LightweightSession();
        await using var second = theStore.LightweightSession();

        var one = await first.Events.FetchForWriting<SimpleAggregate>(streamId);
        theCache.Keys.ShouldBeEmpty();

        var two = await second.Events.FetchForWriting<SimpleAggregate>(streamId);
        ReferenceEquals(one.Aggregate, two.Aggregate).ShouldBeFalse();
    }

    [Fact]
    public async Task a_failed_commit_writes_nothing_back()
    {
        UseCachedAsyncSnapshots();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        await using var loser = theStore.LightweightSession();
        var stream = await loser.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendOne(new CEvent());

        await using (var winner = theStore.LightweightSession())
        {
            winner.Events.Append(streamId, new CEvent());
            await winner.SaveChangesAsync();
        }

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => loser.SaveChangesAsync());

        theCache.Keys.ShouldBeEmpty();

        // ...and a session that survives the failure does not publish it on its next commit
        loser.EjectAllPendingChanges();
        loser.Store(new Target());
        await loser.SaveChangesAsync();

        theCache.Keys.ShouldBeEmpty();
    }

    private void UseCachedAsyncSnapshots()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
        });
    }
}

/// <summary>
///     An aggregate whose Apply returns a new instance rather than changing the one it was given.
/// </summary>
public class ImmutableTally
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public static ImmutableTally Create(AEvent _) => new();

    public ImmutableTally Apply(BEvent _) => new() { Id = Id, Count = Count + 1 };
}
