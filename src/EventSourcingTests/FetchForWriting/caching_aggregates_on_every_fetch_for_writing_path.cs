using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Operations;
using Marten.Metadata;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
///     #6086: <c>CacheAggregatesForWriting&lt;T&gt;()</c> on the fetches other than the plain
///     <c>FetchForWriting(id)</c>: the expected-version overload (what Wolverine's aggregate handler workflow calls
///     when the command carries a version), the batched fetches and <c>FetchManyForWriting</c>, under all three
///     lifecycles. The rules are the plain fetch's (jasperfx#1050): take the entry on read, fold the newer events
///     onto it, put it back after the session's own commit, and fetch uncached when the stream is archived or behind
///     the cached aggregate.
/// </summary>
public class caching_aggregates_on_every_fetch_for_writing_path: OneOffConfigurationsContext
{
    public enum FetchPath
    {
        ExpectedVersion,
        Batched,
        BatchedWithExpectedVersion,
        BatchedExclusive,
        FetchMany
    }

    private readonly MartenTestAggregateWriteCache theCache = new();

    private void configure(ProjectionLifecycle lifecycle, Action<StoreOptions> more = null)
    {
        StoreOptions(opts =>
        {
            switch (lifecycle)
            {
                case ProjectionLifecycle.Inline:
                    opts.Projections.Snapshot<Bug6086Tally>(SnapshotLifecycle.Inline);
                    break;
                case ProjectionLifecycle.Async:
                    opts.Projections.Snapshot<Bug6086Tally>(SnapshotLifecycle.Async);
                    break;
            }

            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<Bug6086Tally>();
            opts.OpenTelemetry.TrackFetchForWritingMetrics();
            more?.Invoke(opts);
        });
    }

    private static async Task<IEventStream<Bug6086Tally>> fetch(IDocumentSession session, FetchPath path,
        Guid streamId, long expectedVersion)
    {
        switch (path)
        {
            case FetchPath.ExpectedVersion:
                return await session.Events.FetchForWriting<Bug6086Tally>(streamId, expectedVersion);

            case FetchPath.FetchMany:
                return (await session.Events.FetchManyForWriting<Bug6086Tally>([streamId])).Single();
        }

        var batch = session.CreateBatchQuery();
        var pending = path switch
        {
            FetchPath.Batched => batch.Events.FetchForWriting<Bug6086Tally>(streamId),
            FetchPath.BatchedWithExpectedVersion => batch.Events.FetchForWriting<Bug6086Tally>(streamId, expectedVersion),
            FetchPath.BatchedExclusive => batch.Events.FetchForExclusiveWriting<Bug6086Tally>(streamId),
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };

        await batch.Execute();
        return await pending;
    }

    private async Task<Guid> startStream(int count)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Bug6086Tally>(streamId, Enumerable.Range(0, count).Select(_ => new AEvent()).ToArray());
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task append(Guid streamId, params object[] events)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, events);
        await session.SaveChangesAsync();
    }

    // A fetch, an append and a commit through the path, which leaves the aggregate in the cache
    private async Task warmUp(FetchPath path, Guid streamId, long version)
    {
        await using var session = theStore.LightweightSession();
        var stream = await fetch(session, path, streamId, version);
        stream.AppendOne(new BEvent());
        await session.SaveChangesAsync();
    }

    public static TheoryData<ProjectionLifecycle, FetchPath> EveryPath()
    {
        var data = new TheoryData<ProjectionLifecycle, FetchPath>();
        foreach (var lifecycle in new[] { ProjectionLifecycle.Live, ProjectionLifecycle.Inline, ProjectionLifecycle.Async })
        {
            foreach (var path in Enum.GetValues<FetchPath>())
            {
                data.Add(lifecycle, path);
            }
        }

        return data;
    }

    public static TheoryData<ProjectionLifecycle, FetchPath> ExpectedVersionPaths()
    {
        var data = new TheoryData<ProjectionLifecycle, FetchPath>();
        foreach (var lifecycle in new[] { ProjectionLifecycle.Live, ProjectionLifecycle.Inline, ProjectionLifecycle.Async })
        {
            data.Add(lifecycle, FetchPath.ExpectedVersion);
            data.Add(lifecycle, FetchPath.BatchedWithExpectedVersion);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPath))]
    public async Task each_hop_of_a_chain_is_served_from_the_cache(ProjectionLifecycle lifecycle, FetchPath path)
    {
        configure(lifecycle);
        var streamId = await startStream(10);

        using var recorder = new EventsReplayedRecorder("marten.fetch_for_writing.events_replayed");

        // Six handlers, one stream, each in its own session and transaction, each appending two events
        for (var hop = 0; hop < 6; hop++)
        {
            await using var session = theStore.LightweightSession();
            var stream = await fetch(session, path, streamId, 10 + 2 * hop);
            stream.Aggregate.ACount.ShouldBe(10 + hop);
            stream.Aggregate.BCount.ShouldBe(hop);
            stream.CurrentVersion.ShouldBe(10 + 2 * hop);

            stream.AppendMany(new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        theCache.Hits.ShouldBe(5);

        long[] replayed;
        lock (recorder.Measurements)
        {
            replayed = recorder.Measurements
                .Where(x => x.AggregateType == typeof(Bug6086Tally).FullNameInCode())
                .Select(x => x.Value)
                .ToArray();
        }

        // The first hop reads the whole stream, and every hop after it only the two events the hop before appended.
        // An Inline aggregate is stored at the stream's version, so it never replays anything: what the cache
        // saves there is the snapshot load, which the next test shows.
        replayed.ShouldBe(lifecycle == ProjectionLifecycle.Inline ? [0, 0, 0, 0, 0, 0] : [10, 2, 2, 2, 2, 2]);
    }

    [Theory]
    [MemberData(nameof(EveryPath))]
    public async Task a_cache_hit_folds_onto_the_cached_aggregate(ProjectionLifecycle lifecycle, FetchPath path)
    {
        configure(lifecycle);
        var streamId = await startStream(3);
        await warmUp(path, streamId, 3);

        // Poison the cached aggregate. Nothing read from the database lands on 100 or more.
        var (_, cachedVersion) = theCache.SingleEntry();
        theCache.Overwrite(theCache.SingleKey(), new Bug6086Tally { Id = streamId, ACount = 100 }, cachedVersion);

        // A Live or Async aggregate is cached at the version it was fetched at, so the warm-up's own event is
        // folded too. An Inline one is cached at the committed version, and has to be exactly at the stream's.
        var expectedACount = 100;
        if (lifecycle != ProjectionLifecycle.Inline)
        {
            await append(streamId, new AEvent(), new AEvent());
            expectedACount = 102;
        }

        await using var session = theStore.LightweightSession();
        var version = lifecycle == ProjectionLifecycle.Inline ? 4 : 6;
        var stream = await fetch(session, path, streamId, version);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(version);
        stream.Aggregate.ACount.ShouldBe(expectedACount);

        stream.AppendOne(new AEvent());
        await session.SaveChangesAsync();

        var (aggregate, written) = theCache.SingleEntry();
        ((Bug6086Tally)aggregate).ACount.ShouldBe(lifecycle == ProjectionLifecycle.Inline ? expectedACount + 1 : expectedACount);
        written.ShouldBe(lifecycle == ProjectionLifecycle.Inline ? version + 1 : version);
    }

    [Theory]
    [MemberData(nameof(EveryPath))]
    public async Task a_cache_ahead_of_the_stream_is_not_used(ProjectionLifecycle lifecycle, FetchPath path)
    {
        configure(lifecycle);
        var streamId = await startStream(3);
        await warmUp(path, streamId, 3);

        // A database restored out from under a cache that had run ahead
        theCache.Overwrite(theCache.SingleKey(), new Bug6086Tally { Id = streamId, ACount = 100 }, 99);

        await using var session = theStore.LightweightSession();
        var stream = await fetch(session, path, streamId, 4);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(4);
        stream.Aggregate.ACount.ShouldBe(3);
        stream.Aggregate.BCount.ShouldBe(1);

        // and the cache heals on the same round, once the session commits
        stream.AppendOne(new AEvent());
        await session.SaveChangesAsync();

        var (aggregate, version) = theCache.SingleEntry();
        version.ShouldBeLessThanOrEqualTo(5);
        ((Bug6086Tally)aggregate).ACount.ShouldBe(version == 5 ? 4 : 3);
    }

    [Theory]
    [MemberData(nameof(EveryPath))]
    public async Task a_stale_entry_still_yields_the_correct_aggregate(ProjectionLifecycle lifecycle, FetchPath path)
    {
        configure(lifecycle);
        var streamId = await startStream(3);
        await warmUp(path, streamId, 3);

        // The stream moves on without the cache
        await append(streamId, new AEvent(), new BEvent(), new AEvent());

        await using var session = theStore.LightweightSession();
        var stream = await fetch(session, path, streamId, 7);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(7);
        stream.Aggregate.ACount.ShouldBe(5);
        stream.Aggregate.BCount.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(ExpectedVersionPaths))]
    public async Task the_expected_version_is_checked_against_the_stream_not_the_cache(ProjectionLifecycle lifecycle,
        FetchPath path)
    {
        configure(lifecycle);
        var streamId = await startStream(3);
        await warmUp(path, streamId, 3);

        // The cached aggregate says the stream is at the version the caller expects. The stream says otherwise.
        var key = theCache.SingleKey();
        theCache.Overwrite(key, new Bug6086Tally { Id = streamId, ACount = 3, BCount = 1 }, 4);
        await append(streamId, new AEvent());

        await using (var session = theStore.LightweightSession())
        {
            await Should.ThrowAsync<ConcurrencyException>(() => fetch(session, path, streamId, 4));
        }

        theCache.Overwrite(key, new Bug6086Tally { Id = streamId, ACount = 3, BCount = 1 }, 4);

        await using (var session = theStore.LightweightSession())
        {
            var stream = await fetch(session, path, streamId, 5);
            stream.CurrentVersion.ShouldBe(5);
            stream.Aggregate.ACount.ShouldBe(4);
            stream.Aggregate.BCount.ShouldBe(1);
        }

        theCache.Hits.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(EveryPath))]
    public async Task cached_aggregates_do_not_cross_tenants(ProjectionLifecycle lifecycle, FetchPath path)
    {
        configure(lifecycle, opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Schema.For<Bug6086Tally>().MultiTenanted();
        });

        // Same stream id, two tenants, different events
        var streamId = Guid.NewGuid();
        foreach (var (tenant, count) in new[] { ("one", 3), ("two", 5) })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream<Bug6086Tally>(streamId,
                Enumerable.Range(0, count).Select(_ => new AEvent()).ToArray());
            await session.SaveChangesAsync();
        }

        var versions = new Dictionary<string, long> { ["one"] = 3, ["two"] = 5 };
        foreach (var tenant in new[] { "one", "two", "one", "two", "one", "two" })
        {
            await using var session = theStore.LightweightSession(tenant);
            var stream = await fetch(session, path, streamId, versions[tenant]);

            var appended = (int)versions[tenant] - (tenant == "one" ? 3 : 5);
            stream.Aggregate.ACount.ShouldBe(tenant == "one" ? 3 : 5);
            stream.Aggregate.BCount.ShouldBe(appended);

            stream.AppendOne(new BEvent());
            await session.SaveChangesAsync();
            versions[tenant]++;
        }

        theCache.Hits.ShouldBe(4);
        theCache.Keys.Select(x => x.TenantId).OrderBy(x => x).ShouldBe(["one", "two"]);
    }

    [Theory]
    [InlineData(FetchPath.ExpectedVersion)]
    [InlineData(FetchPath.Batched)]
    [InlineData(FetchPath.BatchedWithExpectedVersion)]
    [InlineData(FetchPath.BatchedExclusive)]
    [InlineData(FetchPath.FetchMany)]
    public async Task an_archived_live_stream_is_not_served_from_the_cache(FetchPath path)
    {
        configure(ProjectionLifecycle.Live);
        var streamId = await startStream(3);
        await warmUp(path, streamId, 3);

        await using (var archive = theStore.LightweightSession())
        {
            archive.Events.ArchiveStream(streamId);
            await archive.SaveChangesAsync();
        }

        // Its events are left out of a Live read, so the uncached fetch finds no aggregate, and the cached one
        // must not bring it back
        await using var session = theStore.LightweightSession();
        var stream = await fetch(session, path, streamId, 4);

        theCache.Hits.ShouldBe(1);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(4);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Live)]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task a_tombstoned_stream_is_not_served_from_the_cache_in_a_batch(ProjectionLifecycle lifecycle)
    {
        configure(lifecycle);
        var streamId = await startStream(3);
        await warmUp(FetchPath.Batched, streamId, 3);

        // Marked, so an aggregate that came from the cache can be told from one read from the database
        var (_, cachedVersion) = theCache.SingleEntry();
        theCache.Overwrite(theCache.SingleKey(), new Bug6086Tally { Id = streamId, ACount = 100 }, cachedVersion);

        await using (var tombstone = theStore.LightweightSession())
        {
            tombstone.Events.TombstoneStream(streamId);
            await tombstone.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var stream = await fetch(session, FetchPath.Batched, streamId, 0);

        theCache.Hits.ShouldBe(1);
        stream.CurrentVersion.ShouldBe(0);

        // A tombstone leaves an Inline snapshot document where it was, and the uncached fetch reads it, so only
        // the cached aggregate is ruled out there
        if (lifecycle == ProjectionLifecycle.Inline)
        {
            stream.Aggregate?.ACount.ShouldBe(3);
        }
        else
        {
            stream.Aggregate.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Live)]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task fetch_many_from_the_cache_still_queues_the_up_front_lock(ProjectionLifecycle lifecycle)
    {
        var listener = new LockCounter();
        configure(lifecycle, opts => opts.Listeners.Add(listener));

        var one = await startStream(3);
        var two = await startStream(2);
        await warmUp(FetchPath.ExpectedVersion, one, 3);
        await warmUp(FetchPath.ExpectedVersion, two, 2);

        await using var session = theStore.LightweightSession();
        var streams = await session.Events.FetchManyForWriting<Bug6086Tally>([two, one]);

        theCache.Hits.ShouldBe(2);
        streams[0].Aggregate.ACount.ShouldBe(2);
        streams[1].Aggregate.ACount.ShouldBe(3);

        streams[0].AppendOne(new AEvent());
        streams[1].AppendOne(new AEvent());
        await session.SaveChangesAsync();

        // #6083: two streams appended to in one save are locked up front, in a stable order
        listener.Locks.ShouldBe(1);
        listener.LockIsFirst.ShouldBeTrue();
    }

    private class LockCounter: DocumentSessionListenerBase
    {
        public int Locks { get; private set; }
        public bool LockIsFirst { get; private set; }

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            var operations = ((Marten.Internal.Sessions.DocumentSessionBase)session).WorkTracker.AllOperations;
            Locks = operations.Count(x => x is LockStreamsForAppend);
            LockIsFirst = operations.Count > 0 && operations[0] is LockStreamsForAppend;
            return Task.CompletedTask;
        }
    }
}

public class Bug6086Tally: IRevisioned
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }

    public void Apply(AEvent _) => ACount++;
    public void Apply(BEvent _) => BCount++;
}
