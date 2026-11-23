using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;
using Found = EventSourcingTests.FetchForWriting.async_fetch_reads_in_a_safe_order.Found;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// A fetch by natural key resolves the key in every statement it sends. These tests hold the first of them,
/// the version and stream id read, move the key while it is held, then act on what the fetch returned. The fetch
/// must never answer with one stream's id and version and another stream's aggregate.
/// </summary>
public class natural_key_resolution_races: OneOffConfigurationsContext
{
    public enum Lifecycle { Inline, Async, Live }

    public enum Fetch { Latest, ForWriting, ForExclusiveWriting, BatchedLatest, BatchedForWriting }

    public enum Change
    {
        // The key's stream takes another key, and a second stream takes this one
        MovedToAnotherStream,

        // The key's stream gives up its key
        Retired,

        // The key's stream is archived, which frees its key, and a second stream takes it, with no event on
        // the first stream
        ArchivedAndReclaimed
    }

    /// <summary>Holds the key lookup of one batch until <paramref name="whilePaused"/> has run.</summary>
    private sealed class HoldTheKeyLookup(Func<Task> whilePaused): IMartenSessionLogger
    {
        private static long _nextKey = Random.Shared.Next(1, 1 << 30);
        private Task? _pause;

        public Task Paused => _pause ?? Task.FromException(new InvalidOperationException("No fetch was paused"));

        public void OnBeforeExecute(NpgsqlBatch batch)
        {
            if (_pause != null) return;

            var lookup = batch.BatchCommands.Cast<NpgsqlBatchCommand>()
                .FirstOrDefault(c => c.CommandText.StartsWith("select s.version, nk."));
            if (lookup == null) return;

            var key = Interlocked.Increment(ref _nextKey);
            var gate = new NpgsqlConnection(ConnectionSource.ConnectionString);
            gate.Open();
            using (var take = gate.CreateCommand())
            {
                take.CommandText = $"select pg_advisory_lock({key})";
                take.ExecuteNonQuery();
            }

            var where = lookup.CommandText.IndexOf(" where ", StringComparison.Ordinal) + " where ".Length;
            lookup.CommandText = lookup.CommandText.Insert(where,
                $"(select true from pg_advisory_xact_lock_shared({key})) and ");

            _pause = Task.Run(() => pauseAsync(gate, key));
        }

        private async Task pauseAsync(NpgsqlConnection gate, long key)
        {
            try
            {
                await using var waiting = gate.CreateCommand();
                waiting.CommandText =
                    $"select exists (select 1 from pg_locks where locktype = 'advisory' and not granted and objid::bigint = {key})";
                var giveUp = DateTime.UtcNow.AddSeconds(30);
                while (!(bool)(await waiting.ExecuteScalarAsync())!)
                {
                    if (DateTime.UtcNow > giveUp) throw new TimeoutException("The key lookup never reached the pause");
                    await Task.Delay(10);
                }

                await whilePaused();
            }
            finally
            {
                await using (var release = gate.CreateCommand())
                {
                    release.CommandText = $"select pg_advisory_unlock({key})";
                    await release.ExecuteNonQueryAsync();
                }

                await gate.DisposeAsync();
            }
        }

        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlCommand command) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }

    public static IEnumerable<object[]> Cases() =>
        from lifecycle in Enum.GetValues<Lifecycle>()
        from fetch in Enum.GetValues<Fetch>()
        from mapped in new[] { false, true }
        from change in Enum.GetValues<Change>()
        select new object[] { lifecycle, fetch, mapped, change };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task a_key_that_changes_mid_fetch_is_answered_for_one_stream(Lifecycle lifecycle, Fetch fetch,
        bool mapped, Change change)
    {
        var ct = TestContext.Current.CancellationToken;
        StoreOptions(opts =>
        {
            if (mapped) opts.Projections.Snapshot<RaceKeyOwner>(SnapshotLifecycle.Inline);
            if (mapped) register<MappedKeySimpleAggregate>(opts, lifecycle);
            else register<KeyedSimpleAggregate>(opts, lifecycle);
        });

        var key = new RaceKey("K-" + Guid.NewGuid());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using (var setup = theStore.LightweightSession())
        {
            setup.Events.StartStream(first, new KeyedAEvent(key), new BEvent());
            setup.Events.StartStream(second, new KeyedAEvent(new RaceKey("K-" + Guid.NewGuid())), new CEvent(),
                new CEvent());
            await setup.SaveChangesAsync(ct);
        }

        var hold = new HoldTheKeyLookup(async () =>
        {
            await using (var retire = theStore.LightweightSession())
            {
                if (change == Change.ArchivedAndReclaimed) retire.Events.ArchiveStream(first);
                else
                    retire.Events.Append(first,
                        new KeyedAEvent(change == Change.Retired ? new RaceKey(null!) : new RaceKey("K-" + Guid.NewGuid())));
                await retire.SaveChangesAsync(ct);
            }

            if (change != Change.Retired)
            {
                await using var claim = theStore.LightweightSession();
                claim.Events.Append(second, new KeyedAEvent(key));
                await claim.SaveChangesAsync(ct);
            }
        });

        await using var session = theStore.LightweightSession();
        session.Logger = hold;

        Task<(Guid? StreamId, Found? Found, Exception? SaveError)> fetching() => mapped
            ? fetchAsync<MappedKeySimpleAggregate>(session, fetch, key, x => x.Id,
                x => new Found(x.ACount, x.BCount, x.CCount, x.DCount, null), ct)
            : fetchAsync<KeyedSimpleAggregate>(session, fetch, key, x => x.Id,
                x => new Found(x.ACount, x.BCount, x.CCount, x.DCount, null), ct);

        // A batch cannot redo itself, so it reports the key that moved under it rather than answer for two streams
        if (fetch is Fetch.BatchedLatest or Fetch.BatchedForWriting)
        {
            await Should.ThrowAsync<NaturalKeyMovedException>(fetching);
            await hold.Paused;
            return;
        }

        var (streamId, found, saveError) = await fetching();
        await hold.Paused;

        var description =
            $"first {first}, second {second}: fetched stream {streamId?.ToString() ?? "(none)"} with {found}, " +
            $"save {(saveError == null ? "succeeded" : saveError.GetType().Name + ": " + saveError.Message)}";
        saveError.ShouldBeNull(description);

        // The fetch is redone, and answers for the key as it is after the change: the second stream, with its A,
        // two Cs and the A that claimed the key, at version 4, or no stream once the key is retired. A write
        // appended to the stream at the version of the state it was handed.
        // The one exception is an exclusive fetch of a stream archived while it waited for the row lock: the
        // lock finds no live stream, so the key named none at the start, and the fetch starts a new stream.
        var write = isWrite(fetch);
        var noStream = change == Change.Retired ||
                       (change == Change.ArchivedAndReclaimed && fetch == Fetch.ForExclusiveWriting);
        (Guid?, Found?) expected = noStream
            ? (null, write ? new Found(0, 0, 0, 0, 0) : null)
            : (second, new Found(2, 0, 2, 0, write ? 4 : null));

        ((Guid?)streamId, found).ShouldBe(expected, description);
    }

    public static IEnumerable<object[]> Claimants() =>
        from lifecycle in Enum.GetValues<Lifecycle>()
        from renaming in new[] { false, true }
        select new object[] { lifecycle, renaming };

    /// <summary>
    /// Many sessions commit a stream with the same key at once, some by starting a new stream with it and,
    /// with <paramref name="renaming" />, some by renaming an existing stream to it. Exactly one may win.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claimants))]
    public async Task concurrent_claims_of_one_key_have_one_winner(Lifecycle lifecycle, bool renaming)
    {
        var ct = TestContext.Current.CancellationToken;
        StoreOptions(opts => register<KeyedSimpleAggregate>(opts, lifecycle));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        for (var round = 0; round < 10; round++)
        {
            var key = new RaceKey("K-" + Guid.NewGuid());
            var existing = new List<Guid>();
            if (renaming)
            {
                await using var setup = theStore.LightweightSession();
                for (var i = 0; i < 8; i++)
                {
                    var id = Guid.NewGuid();
                    setup.Events.StartStream(id, new KeyedAEvent(new RaceKey("K-" + Guid.NewGuid())));
                    existing.Add(id);
                }

                await setup.SaveChangesAsync(ct);
            }

            var gate = new TaskCompletionSource();
            var attempts = Enumerable.Range(0, 16).Select(i => Task.Run(async () =>
            {
                await using var session = theStore.LightweightSession();
                var streamId = renaming && i % 2 == 0 ? existing[i / 2] : Guid.NewGuid();
                if (renaming && i % 2 == 0) session.Events.Append(streamId, new KeyedAEvent(key));
                else session.Events.StartStream(streamId, new KeyedAEvent(key));
                await gate.Task;
                try
                {
                    await session.SaveChangesAsync(ct);
                    return (streamId, (Exception?)null);
                }
                catch (Exception e)
                {
                    return (streamId, e);
                }
            }, ct)).ToArray();

            gate.SetResult();
            var results = await Task.WhenAll(attempts);

            var winners = results.Where(x => x.Item2 == null).Select(x => x.streamId).ToList();
            winners.Count.ShouldBe(1, string.Join("; ", results.Select(x => x.Item2?.GetType().Name ?? "won")));
            results.Where(x => x.Item2 != null).ShouldAllBe(x => x.Item2 is DuplicateNaturalKeyException);

            await using var query = theStore.LightweightSession();
            var stream = await query.Events.FetchForWriting<KeyedSimpleAggregate, RaceKey>(key, ct);
            stream.Id.ShouldBe(winners.Single());
            stream.Aggregate!.Key.ShouldBe(key);
        }
    }

    private static bool isWrite(Fetch fetch) => fetch is Fetch.ForWriting or Fetch.ForExclusiveWriting
        or Fetch.BatchedForWriting;

    private static void register<T>(StoreOptions opts, Lifecycle lifecycle) where T : class
    {
        switch (lifecycle)
        {
            case Lifecycle.Inline:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline);
                break;
            case Lifecycle.Async:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Async);
                break;
            default:
                opts.Projections.LiveStreamAggregation<T>();
                break;
        }
    }

    private static async Task<(Guid? StreamId, Found? Found, Exception? SaveError)> fetchAsync<T>(
        IDocumentSession session, Fetch fetch, RaceKey key, Func<T, Guid> idOf, Func<T, Found> countsOf,
        CancellationToken ct) where T : class, new()
    {
        IEventStream<T> stream;
        switch (fetch)
        {
            case Fetch.Latest:
            {
                var latest = await session.Events.FetchLatest<T, RaceKey>(key, ct);
                return latest == null ? (null, null, null) : (idOf(latest), countsOf(latest), null);
            }
            case Fetch.BatchedLatest:
            {
                var batch = session.CreateBatchQuery();
                var pending = batch.Events.FetchLatest<T, RaceKey>(key);
                await batch.Execute(ct);
                var latest = await pending;
                return latest == null ? (null, null, null) : (idOf(latest), countsOf(latest), null);
            }
            case Fetch.ForWriting:
                stream = await session.Events.FetchForWriting<T, RaceKey>(key, ct);
                break;
            case Fetch.ForExclusiveWriting:
                stream = await session.Events.FetchForExclusiveWriting<T, RaceKey>(key, ct);
                break;
            default:
            {
                var batch = session.CreateBatchQuery();
                var pending = batch.Events.FetchForWriting<T, RaceKey>(key);
                await batch.Execute(ct);
                stream = await pending;
                break;
            }
        }

        var counts = stream.Aggregate == null ? new Found(0, 0, 0, 0, null) : countsOf(stream.Aggregate);
        var found = counts with { Version = stream.CurrentVersion ?? 0 };
        Guid? id = stream.CurrentVersion is null or 0 ? null : stream.Id;

        // Act on what was fetched, as a command would
        stream.AppendOne(new DEvent());
        try
        {
            await session.SaveChangesAsync(ct);
            return (id, found, null);
        }
        catch (Exception e)
        {
            return (id, found, e);
        }
    }
}
