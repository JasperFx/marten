#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// Several writers append to the same Async-snapshotted streams through every kind of fetch for writing,
/// while readers fetch the same streams and the snapshot moves underneath all of them. Every event records its
/// own version in the aggregate, so a fetch that applies an event twice or misses one shows up as a repeat or
/// a gap, and a fetch that comes back behind an append that had already committed shows up as too short.
/// </summary>
/// <remarks>
/// The snapshot moves two ways. With the daemon running, it only moves forward, as in production. With the
/// daemon off, a task deletes it or rewrites it at an earlier version, which is what a rebuild does to it.
/// The rewritten snapshot is always correct for the version it claims, folded live from the events, so any
/// wrong answer is the fetch's. The daemon is never left running beside the rewinds: it would fold new events
/// onto a rewound snapshot, and the wrong snapshot would be the test's own doing.
/// </remarks>
public abstract class async_fetch_under_concurrent_writes: OneOffConfigurationsContext
{
    private const int Writers = 4;
    private const int AppendsPerWriter = 10;
    private const int Readers = 2;

    public enum SnapshotMovement
    {
        DaemonRunning,
        DaemonRunningWithTheWriteCache,
        RewoundAndDeleted,

        /// <summary>
        /// #5225: no snapshot at all. The aggregate is Live and enrolled in the write cache, so every fetch for
        /// writing starts from whatever the cache holds and reads only the events after it.
        /// </summary>
        LiveWithTheWriteCache
    }

    public enum WriteFetch
    {
        ForWriting,
        ForExclusiveWriting,
        BatchedForWriting,
        ManyForWriting
    }

    public enum ReadFetch
    {
        Latest,
        StreamLatestJson,
        BatchedLatest
    }

    /// <summary>One stream as a fetch found it: the event versions folded into the aggregate, and its version.</summary>
    public readonly record struct Fetched(object Id, IReadOnlyList<long>? Seen, long? Version);

    private readonly TallySubject _subject;

    protected async_fetch_under_concurrent_writes(TallySubject subject)
    {
        _subject = subject;
    }

    [Theory]
    [InlineData(SnapshotMovement.DaemonRunning)]
    [InlineData(SnapshotMovement.DaemonRunningWithTheWriteCache)]
    [InlineData(SnapshotMovement.RewoundAndDeleted)]
    [InlineData(SnapshotMovement.LiveWithTheWriteCache)]
    public async Task no_event_is_lost_or_applied_twice(SnapshotMovement movement)
    {
        var live = movement == SnapshotMovement.LiveWithTheWriteCache;
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = false;
            _subject.Configure(opts, movement is SnapshotMovement.DaemonRunningWithTheWriteCache or SnapshotMovement.LiveWithTheWriteCache,
                live);
        });

        var ct = TestContext.Current.CancellationToken;

        // The snapshot table has to exist before anything writes to it behind Marten's back
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var ids = new[] { _subject.NewId(), _subject.NewId() };
        var committed = new long[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            await using var s = theStore.LightweightSession();
            _subject.Start(s, ids[i], new Tick(-1));
            await s.SaveChangesAsync(ct);
            committed[i] = 1;
        }

        var failures = new ConcurrentQueue<string>();
        var appends = 0;
        var refusedBatches = 0;
        var rewinds = 0;

        var daemon = movement is SnapshotMovement.RewoundAndDeleted or SnapshotMovement.LiveWithTheWriteCache
            ? null
            : await theStore.BuildProjectionDaemonAsync();
        if (daemon != null) await daemon.StartAllAsync();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        void check(string what, Fetched fetched, long atLeast)
        {
            var problem = describe(fetched, atLeast);
            if (problem != null) failures.Enqueue($"{what} of {fetched.Id}: {problem}");
        }

        async Task writeAsync(int writer)
        {
            var done = 0;
            for (var attempt = 0; done < AppendsPerWriter && attempt < AppendsPerWriter * 50; attempt++)
            {
                var target = (writer + done) % ids.Length;
                var kind = (WriteFetch)((writer + attempt) % Enum.GetValues<WriteFetch>().Length);
                var floors = new long[ids.Length];
                for (var i = 0; i < ids.Length; i++) floors[i] = Interlocked.Read(ref committed[i]);

                try
                {
                    await using var s = theStore.LightweightSession();
                    var found = await _subject.FetchForWritingAsync(s, kind, ids[target], ids[1 - target], ct);
                    foreach (var stream in found)
                    {
                        check($"{kind} by writer {writer}", stream, floors[Array.IndexOf(ids, stream.Id)]);
                    }

                    _subject.Append(s, ids[target], new Tick(writer));
                    await s.SaveChangesAsync(ct);

                    var written = found[0].Version!.Value + 1;
                    long seen;
                    while ((seen = Interlocked.Read(ref committed[target])) < written &&
                           Interlocked.CompareExchange(ref committed[target], written, seen) != seen)
                    {
                    }

                    Interlocked.Increment(ref appends);
                    done++;
                }
                catch (Exception e) when (e is ConcurrencyException or EventStreamUnexpectedMaxEventIdException)
                {
                    // Another writer got there first. Fetch again.
                }
                catch (SnapshotRaceException e) when (kind == WriteFetch.BatchedForWriting
                                                      && movement == SnapshotMovement.RewoundAndDeleted)
                {
                    // A batch cannot redo a fetch whose snapshot went backwards between its reads, so it refuses.
                    // That is only allowed when something moved the snapshot backwards.
                    e.Id.ShouldNotBeNull();
                    Interlocked.Increment(ref refusedBatches);
                }
                catch (Exception e)
                {
                    failures.Enqueue($"{kind} by writer {writer}: {e}");
                    return;
                }
            }

            if (done < AppendsPerWriter) failures.Enqueue($"Writer {writer} only appended {done} times");
        }

        async Task readAsync(int reader)
        {
            for (var attempt = 0; !stop.IsCancellationRequested; attempt++)
            {
                var target = (reader + attempt) % ids.Length;
                var kind = (ReadFetch)(attempt % Enum.GetValues<ReadFetch>().Length);
                var floor = Interlocked.Read(ref committed[target]);

                try
                {
                    await using var s = theStore.LightweightSession();
                    var found = await _subject.FetchForReadingAsync(theStore, s, kind, ids[target], ct);
                    check($"{kind} by reader {reader}", found, floor);
                }
                catch (SnapshotRaceException) when (kind == ReadFetch.BatchedLatest
                                                    && movement == SnapshotMovement.RewoundAndDeleted)
                {
                    Interlocked.Increment(ref refusedBatches);
                }
                catch (Exception e)
                {
                    failures.Enqueue($"{kind} by reader {reader}: {e}");
                    return;
                }
            }
        }

        async Task rewindAsync()
        {
            var random = new Random(17);
            while (!stop.IsCancellationRequested)
            {
                var target = random.Next(ids.Length);
                var current = Interlocked.Read(ref committed[target]);

                try
                {
                    if (random.Next(3) == 0)
                    {
                        await _subject.DeleteSnapshotAsync(theStore, SchemaName, ids[target], ct);
                    }
                    else
                    {
                        await _subject.WriteSnapshotAsync(theStore, SchemaName, ids[target],
                            random.NextInt64(1, current + 1), ct);
                    }

                    Interlocked.Increment(ref rewinds);
                    await Task.Delay(random.Next(2, 8), ct);
                }
                catch (Exception e)
                {
                    failures.Enqueue($"Moving the snapshot: {e}");
                    return;
                }
            }
        }

        var writers = Enumerable.Range(0, Writers).Select(w => Task.Run(() => writeAsync(w), ct)).ToArray();
        var others = Enumerable.Range(0, Readers).Select(r => Task.Run(() => readAsync(r), ct)).ToList();
        if (movement == SnapshotMovement.RewoundAndDeleted) others.Add(Task.Run(rewindAsync, ct));

        try
        {
            await Task.WhenAll(writers);
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(others);
            if (daemon != null)
            {
                await daemon.StopAllAsync();
                daemon.Dispose();
            }
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
        appends.ShouldBe(Writers * AppendsPerWriter);
        if (movement == SnapshotMovement.RewoundAndDeleted) rewinds.ShouldBeGreaterThan(0);

        // Settled: every append is in its stream exactly once, through every kind of fetch, with the snapshot
        // caught up where there is a daemon to catch it up and wherever it was left where there is not
        if (daemon != null)
        {
            using var catchUp = await theStore.BuildProjectionDaemonAsync();
            await catchUp.CatchUpAsync(ct);
        }

        committed.Sum().ShouldBe(ids.Length + Writers * AppendsPerWriter);
        for (var i = 0; i < ids.Length; i++)
        {
            await using var s = theStore.LightweightSession();
            foreach (var kind in Enum.GetValues<WriteFetch>())
            {
                var found = await _subject.FetchForWritingAsync(s, kind, ids[i], ids[1 - i], ct);
                describe(found[0], committed[i]).ShouldBeNull();
                found[0].Version.ShouldBe(committed[i]);
            }

            foreach (var kind in Enum.GetValues<ReadFetch>())
            {
                var found = await _subject.FetchForReadingAsync(theStore, s, kind, ids[i], ct);
                describe(found, committed[i]).ShouldBeNull();
                found.Seen!.Count.ShouldBe((int)committed[i]);
            }
        }
    }

    /// <summary>What is wrong with a fetch's answer, or null when the events folded are exactly 1..version.</summary>
    private static string? describe(Fetched fetched, long atLeast)
    {
        var seen = fetched.Seen;
        if (seen == null) return "no aggregate for a stream that has events";

        for (var i = 0; i < seen.Count; i++)
        {
            if (seen[i] != i + 1)
            {
                return $"the aggregate folded versions [{string.Join(", ", seen)}], not 1..{seen.Count}";
            }
        }

        if (fetched.Version.HasValue && fetched.Version.Value != seen.Count)
        {
            return $"the stream is at version {fetched.Version} but the aggregate folded {seen.Count} events";
        }

        if (seen.Count < atLeast)
        {
            return $"the aggregate folded {seen.Count} events, but {atLeast} had already been committed";
        }

        return null;
    }
}

public record Tick(int Writer);

public class TickTally: IRevisioned
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public List<long> Seen { get; set; } = new();

    public void Apply(IEvent<Tick> e) => Seen.Add(e.Version);
}

public class TickTallyAsString: IRevisioned
{
    public string Id { get; set; } = null!;
    public int Version { get; set; }
    public List<long> Seen { get; set; } = new();

    public void Apply(IEvent<Tick> e) => Seen.Add(e.Version);
}

public readonly record struct TickTallyId(Guid Value);

public class TickTallyWithAStrongTypedId: IRevisioned
{
    public TickTallyId Id { get; set; }
    public int Version { get; set; }
    public List<long> Seen { get; set; } = new();

    public void Apply(IEvent<Tick> e) => Seen.Add(e.Version);
}

/// <summary>Drives one tally aggregate through the fetches, by Guid or string stream identity.</summary>
public abstract class TallySubject
{
    /// <param name="live">No snapshot: the aggregate is built Live on every fetch (#5225).</param>
    public abstract void Configure(StoreOptions opts, bool writeCache, bool live = false);
    public abstract object NewId();
    public abstract void Start(IDocumentSession session, object id, Tick tick);
    public abstract void Append(IDocumentSession session, object id, Tick tick);

    /// <summary>The fetched stream first, then any other stream the same fetch read.</summary>
    public abstract Task<IReadOnlyList<async_fetch_under_concurrent_writes.Fetched>> FetchForWritingAsync(
        IDocumentSession session, async_fetch_under_concurrent_writes.WriteFetch fetch, object id, object otherId,
        CancellationToken ct);

    public abstract Task<async_fetch_under_concurrent_writes.Fetched> FetchForReadingAsync(IDocumentStore store,
        IDocumentSession session, async_fetch_under_concurrent_writes.ReadFetch fetch, object id, CancellationToken ct);

    public abstract Task DeleteSnapshotAsync(IDocumentStore store, string schema, object id, CancellationToken ct);

    /// <summary>Replaces the snapshot with the stream folded live up to <paramref name="version" />.</summary>
    public abstract Task WriteSnapshotAsync(IDocumentStore store, string schema, object id, long version,
        CancellationToken ct);
}

public sealed class TallySubject<T>(
    string table,
    Action<StoreOptions> configure,
    Func<object> newId,
    Func<T, IReadOnlyList<long>> seenOf): TallySubject where T : class
{
    public override void Configure(StoreOptions opts, bool writeCache, bool live = false)
    {
        configure(opts);
        if (!live) opts.Projections.Snapshot<T>(SnapshotLifecycle.Async);
        if (writeCache) opts.Events.CacheAggregatesForWriting<T>();
    }

    public override object NewId() => newId();

    public override void Start(IDocumentSession session, object id, Tick tick)
    {
        if (id is Guid guid) session.Events.StartStream<T>(guid, tick);
        else session.Events.StartStream<T>((string)id, tick);
    }

    public override void Append(IDocumentSession session, object id, Tick tick)
    {
        if (id is Guid guid) session.Events.Append(guid, tick);
        else session.Events.Append((string)id, tick);
    }

    private async_fetch_under_concurrent_writes.Fetched found(object id, T? aggregate, long? version) =>
        new(id, aggregate == null ? null : seenOf(aggregate).ToList(), version);

    private async_fetch_under_concurrent_writes.Fetched found(object id, IEventStream<T> stream) =>
        found(id, stream.Aggregate, stream.CurrentVersion);

    public override async Task<IReadOnlyList<async_fetch_under_concurrent_writes.Fetched>> FetchForWritingAsync(
        IDocumentSession session, async_fetch_under_concurrent_writes.WriteFetch fetch, object id, object otherId,
        CancellationToken ct)
    {
        var events = session.Events;
        switch (fetch)
        {
            case async_fetch_under_concurrent_writes.WriteFetch.ForWriting:
                return [found(id, id is Guid g1 ? await events.FetchForWriting<T>(g1, ct) : await events.FetchForWriting<T>((string)id, ct))];

            case async_fetch_under_concurrent_writes.WriteFetch.ForExclusiveWriting:
                return [found(id, id is Guid g2
                    ? await events.FetchForExclusiveWriting<T>(g2, ct)
                    : await events.FetchForExclusiveWriting<T>((string)id, ct))];

            case async_fetch_under_concurrent_writes.WriteFetch.BatchedForWriting:
            {
                // Behind another fetch, so neither is first in its batch
                var batch = session.CreateBatchQuery();
                var other = otherId is Guid og ? batch.Events.FetchForWriting<T>(og) : batch.Events.FetchForWriting<T>((string)otherId);
                var mine = id is Guid g3 ? batch.Events.FetchForWriting<T>(g3) : batch.Events.FetchForWriting<T>((string)id);
                await batch.Execute(ct);
                return [found(id, await mine), found(otherId, await other)];
            }

            case async_fetch_under_concurrent_writes.WriteFetch.ManyForWriting:
            {
                var streams = id is Guid g4
                    ? await events.FetchManyForWriting<T>(new[] { g4, (Guid)otherId }, ct)
                    : await events.FetchManyForWriting<T>(new[] { (string)id, (string)otherId }, ct);
                return [found(id, streams[0]), found(otherId, streams[1])];
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(fetch));
        }
    }

    public override async Task<async_fetch_under_concurrent_writes.Fetched> FetchForReadingAsync(IDocumentStore store,
        IDocumentSession session, async_fetch_under_concurrent_writes.ReadFetch fetch, object id, CancellationToken ct)
    {
        switch (fetch)
        {
            case async_fetch_under_concurrent_writes.ReadFetch.Latest:
                return found(id, id is Guid g1
                    ? await session.Events.FetchLatest<T>(g1, ct)
                    : await session.Events.FetchLatest<T>((string)id, ct), null);

            case async_fetch_under_concurrent_writes.ReadFetch.StreamLatestJson:
            {
                using var json = new MemoryStream();
                var any = id is Guid g2
                    ? await session.Events.StreamLatestJson<T>(g2, json, ct)
                    : await session.Events.StreamLatestJson<T>((string)id, json, ct);
                if (!any) return found(id, null, null);

                json.Position = 0;
                return found(id, store.Options.Serializer().FromJson<T>(json), null);
            }

            case async_fetch_under_concurrent_writes.ReadFetch.BatchedLatest:
            {
                var batch = session.CreateBatchQuery();
                var latest = id is Guid g3 ? batch.Events.FetchLatest<T>(g3) : batch.Events.FetchLatest<T>((string)id);
                await batch.Execute(ct);
                return found(id, await latest, null);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(fetch));
        }
    }

    public override async Task DeleteSnapshotAsync(IDocumentStore store, string schema, object id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"delete from {schema}.{table} where id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public override async Task WriteSnapshotAsync(IDocumentStore store, string schema, object id, long version,
        CancellationToken ct)
    {
        T? folded;
        await using (var session = store.QuerySession())
        {
            folded = id is Guid guid
                ? await session.Events.AggregateStreamAsync<T>(guid, version, token: ct)
                : await session.Events.AggregateStreamAsync<T>((string)id, version, token: ct);
        }

        if (folded == null) return;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"insert into {schema}.{table} (id, data, mt_version, mt_dotnet_type) " +
                          "values (@id, @data::jsonb, @version, @type) " +
                          "on conflict (id) do update set data = excluded.data, mt_version = excluded.mt_version";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("data", store.Options.Serializer().ToJson(folded));
        cmd.Parameters.AddWithValue("version", (int)seenOf(folded).Count);
        cmd.Parameters.AddWithValue("type", typeof(T).FullName!);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public class async_fetch_under_concurrent_writes_on_guid_streams(): async_fetch_under_concurrent_writes(
    new TallySubject<TickTally>("mt_doc_ticktally", _ => { }, () => Guid.NewGuid(), x => x.Seen));

public class async_fetch_under_concurrent_writes_on_string_streams(): async_fetch_under_concurrent_writes(
    new TallySubject<TickTallyAsString>("mt_doc_ticktallyasstring",
        opts => opts.Events.StreamIdentity = StreamIdentity.AsString,
        () => Guid.NewGuid().ToString(), x => x.Seen));

public class async_fetch_under_concurrent_writes_with_a_strong_typed_id(): async_fetch_under_concurrent_writes(
    new TallySubject<TickTallyWithAStrongTypedId>("mt_doc_ticktallywithastrongtypedid",
        opts => opts.RegisterValueType<TickTallyId>(),
        () => Guid.NewGuid(), x => x.Seen));
