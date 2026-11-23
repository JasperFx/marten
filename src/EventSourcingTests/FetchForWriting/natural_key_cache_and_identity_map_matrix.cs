using System;
using System.Collections.Generic;
using System.Linq;
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

public enum KeyChange
{
    Renamed,

    /// <summary>The key set back to a value of null, which retires it.</summary>
    Retired
}

/// <summary>How a key change is appended.</summary>
public enum ChangedThrough
{
    /// <summary>A fetch for writing by stream id, which goes through the write cache.</summary>
    Fetch,

    /// <summary>A plain append, which leaves the key's pointer to the cache stale.</summary>
    Append
}

public enum ByWhat
{
    StreamId,
    NaturalKey,
    BatchedStreamId,
    BatchedNaturalKey
}

/// <summary>
/// The write cache and the identity map answer a fetch whichever way the aggregate was first fetched, by stream
/// id or by natural key, and whichever way it is fetched again. Every fetch for writing, batched or not, claims
/// the stream's write cache entry and writes the aggregate back after its commit. A batched fetch by natural key
/// claims the entry once the batch has said which stream the key names, and reads that stream uncached, because a
/// batch cannot redo one fetch after a stale pointer. A batched FetchLatest of an Async or Live aggregate does not
/// fold the session's pending events, as by stream id.
/// </summary>
public class natural_key_cache_and_identity_map_matrix: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    public static IEnumerable<object[]> WriteCacheCases() =>
        from lifecycle in new[] { MatrixLifecycle.Inline, MatrixLifecycle.Async, MatrixLifecycle.Live }
        from seed in new[] { ByWhat.StreamId, ByWhat.NaturalKey, ByWhat.BatchedStreamId, ByWhat.BatchedNaturalKey }
        from take in new[] { ByWhat.StreamId, ByWhat.NaturalKey, ByWhat.BatchedStreamId, ByWhat.BatchedNaturalKey }
        select new object[] { lifecycle, seed, take };

    public static IEnumerable<object[]> IdentityMapCases() =>
        from lifecycle in new[] { MatrixLifecycle.Inline, MatrixLifecycle.Async, MatrixLifecycle.Live }
        from first in new[] { ByWhat.StreamId, ByWhat.NaturalKey }
        from then in new[] { ByWhat.StreamId, ByWhat.NaturalKey, ByWhat.BatchedStreamId, ByWhat.BatchedNaturalKey }
        select new object[] { lifecycle, first, then };

    private static void registerFor(StoreOptions opts, MatrixLifecycle lifecycle)
    {
        if (lifecycle == MatrixLifecycle.Live)
        {
            opts.Projections.LiveStreamAggregation<KeyedSimpleAggregate>();
        }
        else
        {
            opts.Projections.Snapshot<KeyedSimpleAggregate>(
                lifecycle == MatrixLifecycle.Inline ? SnapshotLifecycle.Inline : SnapshotLifecycle.Async);
        }
    }

    private async Task<Guid> startAsync()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<KeyedSimpleAggregate>(streamId, new KeyedAEvent(RaceKey.For(streamId)),
            new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();
        return streamId;
    }

    private static async Task<IEventStream<KeyedSimpleAggregate>> fetchForWritingAsync(IDocumentSession session,
        ByWhat by, Guid streamId)
    {
        var key = RaceKey.For(streamId);
        switch (by)
        {
            case ByWhat.StreamId:
                return await session.Events.FetchForWriting<KeyedSimpleAggregate>(streamId);
            case ByWhat.NaturalKey:
                return await session.Events.FetchForWriting<KeyedSimpleAggregate, RaceKey>(key);
        }

        var batch = session.CreateBatchQuery();
        var fetched = by == ByWhat.BatchedStreamId
            ? batch.Events.FetchForWriting<KeyedSimpleAggregate>(streamId)
            : batch.Events.FetchForWriting<KeyedSimpleAggregate, RaceKey>(key);
        await batch.Execute();
        return await fetched;
    }

    private static async Task<KeyedSimpleAggregate?> fetchLatestAsync(IDocumentSession session, ByWhat by,
        Guid streamId)
    {
        var key = RaceKey.For(streamId);
        switch (by)
        {
            case ByWhat.StreamId:
                return await session.Events.FetchLatest<KeyedSimpleAggregate>(streamId);
            case ByWhat.NaturalKey:
                return await session.Events.FetchLatest<KeyedSimpleAggregate, RaceKey>(key);
        }

        var batch = session.CreateBatchQuery();
        var fetched = by == ByWhat.BatchedStreamId
            ? batch.Events.FetchLatest<KeyedSimpleAggregate>(streamId)
            : batch.Events.FetchLatest<KeyedSimpleAggregate, RaceKey>(key);
        await batch.Execute();
        return await fetched;
    }

    [Theory]
    [MemberData(nameof(WriteCacheCases))]
    public async Task the_write_cache_is_taken_however_it_was_seeded(MatrixLifecycle lifecycle, ByWhat seed,
        ByWhat take)
    {
        StoreOptions(opts =>
        {
            registerFor(opts, lifecycle);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<KeyedSimpleAggregate>();
        });

        var streamId = await startAsync();

        // The commit writes the aggregate back to the cache, and points its key at it
        await using (var seeding = theStore.LightweightSession())
        {
            var stream = await fetchForWritingAsync(seeding, seed, streamId);
            stream.AppendOne(new BEvent());
            await seeding.SaveChangesAsync();
        }

        var hits = theCache.Hits;

        await using (var taking = theStore.LightweightSession())
        {
            var stream = await fetchForWritingAsync(taking, take, streamId);

            theCache.Hits.ShouldBe(hits + 1);
            stream.Aggregate!.BCount.ShouldBe(2);
            stream.CurrentVersion.ShouldBe(4);

            stream.AppendOne(new CEvent());
            await taking.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        var latest = await query.Events.FetchLatest<KeyedSimpleAggregate, RaceKey>(RaceKey.For(streamId));
        latest!.CCount.ShouldBe(2);
        latest.BCount.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(IdentityMapCases))]
    public async Task the_identity_map_answers_however_the_aggregate_was_fetched(MatrixLifecycle lifecycle,
        ByWhat first, ByWhat then)
    {
        StoreOptions(opts =>
        {
            if (lifecycle == MatrixLifecycle.Live) opts.Projections.LiveStreamAggregation<KeyedSimpleAggregate>();
            else opts.Projections.Snapshot<KeyedSimpleAggregate>(
                lifecycle == MatrixLifecycle.Inline ? SnapshotLifecycle.Inline : SnapshotLifecycle.Async);
        });

        var streamId = await startAsync();

        await using var session = lifecycle == MatrixLifecycle.Inline
            ? theStore.IdentitySession()
            : theStore.LightweightSession();

        var stream = await fetchForWritingAsync(session, first, streamId);

        // Appended in this session, so a read that went to the database would not see it
        stream.AppendOne(new DEvent());

        var before = session.RequestCount;
        var latest = await fetchLatestAsync(session, then, streamId);

        latest.ShouldNotBeNull();
        latest.Id.ShouldBe(streamId);

        if (then is ByWhat.StreamId or ByWhat.NaturalKey)
        {
            session.RequestCount.ShouldBe(before);
        }

        if (lifecycle == MatrixLifecycle.Inline)
        {
            // The same instance the fetch for writing loaded; its pending events are applied at commit
            latest.ShouldBeSameAs(stream.Aggregate);
        }
        else if (then is ByWhat.StreamId or ByWhat.NaturalKey)
        {
            // Built from the fetched stream, with this session's pending events folded in
            latest.DCount.ShouldBe(1);
            latest.BCount.ShouldBe(1);
        }
        else
        {
            // Read in the batch, which leaves the session's pending events out
            latest.DCount.ShouldBe(0);
            latest.BCount.ShouldBe(1);
        }
    }

    public static IEnumerable<object[]> KeyChangeCases() =>
        from lifecycle in new[] { MatrixLifecycle.Inline, MatrixLifecycle.Async, MatrixLifecycle.Live }
        from seed in new[] { ByWhat.StreamId, ByWhat.NaturalKey }
        from change in Enum.GetValues<KeyChange>()
        from through in Enum.GetValues<ChangedThrough>()
        select new object[] { lifecycle, seed, change, through };

    [Theory]
    [MemberData(nameof(KeyChangeCases))]
    public async Task a_changed_key_is_not_answered_by_its_old_value(MatrixLifecycle lifecycle, ByWhat seed,
        KeyChange change, ChangedThrough through)
    {
        StoreOptions(opts =>
        {
            registerFor(opts, lifecycle);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<KeyedSimpleAggregate>();
        });

        var streamId = await startAsync();
        var oldKey = RaceKey.For(streamId);

        // Cached, with the old key pointing at it
        await using (var seeding = theStore.LightweightSession())
        {
            var stream = await fetchForWritingAsync(seeding, seed, streamId);
            stream.AppendOne(new BEvent());
            await seeding.SaveChangesAsync();
        }

        var newKey = change == KeyChange.Renamed ? new RaceKey("K-NEW-" + streamId) : new RaceKey(null!);
        await using (var changing = theStore.LightweightSession())
        {
            if (through == ChangedThrough.Fetch)
            {
                var stream = await changing.Events.FetchForWriting<KeyedSimpleAggregate>(streamId);
                stream.AppendOne(new KeyedAEvent(newKey));
            }
            else
            {
                changing.Events.Append(streamId, new KeyedAEvent(newKey));
            }

            await changing.SaveChangesAsync();
        }

        if (change == KeyChange.Retired)
        {
            // Another stream claims the key the first one gave up
            var claimant = Guid.NewGuid();
            theSession.Events.StartStream<KeyedSimpleAggregate>(claimant, new KeyedAEvent(oldKey), new CEvent());
            await theSession.SaveChangesAsync();

            await using var claiming = theStore.LightweightSession();
            var claimed = await claiming.Events.FetchForWriting<KeyedSimpleAggregate, RaceKey>(oldKey);
            claimed.Id.ShouldBe(claimant);
            claimed.CurrentVersion.ShouldBe(2);
            claimed.Aggregate!.CCount.ShouldBe(1);
            claimed.Aggregate.BCount.ShouldBe(0);
            (await claiming.Events.FetchLatest<KeyedSimpleAggregate, RaceKey>(oldKey))!.Id.ShouldBe(claimant);
            return;
        }

        await using var session = theStore.LightweightSession();

        // No stream has the old key, so this is a new stream...
        var byOldKey = await session.Events.FetchForWriting<KeyedSimpleAggregate, RaceKey>(oldKey);
        byOldKey.Aggregate.ShouldBeNull();
        byOldKey.CurrentVersion.ShouldBe(0);
        (await session.Events.FetchLatest<KeyedSimpleAggregate, RaceKey>(oldKey)).ShouldBeNull();

        // ...and whatever the old key's pointer led to is still in the cache for its own stream
        theCache.Keys.ShouldContain(x => streamId.Equals(x.Id));

        var byNewKey = await session.Events.FetchForWriting<KeyedSimpleAggregate, RaceKey>(newKey);
        byNewKey.Id.ShouldBe(streamId);
        byNewKey.CurrentVersion.ShouldBe(5);
        byNewKey.Aggregate!.ACount.ShouldBe(2);
        byNewKey.Aggregate.BCount.ShouldBe(2);
        byNewKey.Aggregate.Key.ShouldBe(newKey);
    }
}
