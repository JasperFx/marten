using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// An Async-lifecycle fetch reads the events after the snapshot and the snapshot document as separate
/// statements, and each takes its own snapshot of the database. These tests hold the first of those two
/// reads, change the database while it is held, and check that every kind of fetch still answers rightly.
/// Each subclass runs the same scenarios against a different store setup.
/// </summary>
public abstract class async_fetch_reads_in_a_safe_order: OneOffConfigurationsContext
{
    /// <summary>
    /// Holds whichever of the events read and the snapshot read comes first, for one fetch in a batch, until
    /// <paramref name="whilePaused" /> has run. The held read waits on an advisory lock this logger takes
    /// before the batch is sent, and gets it only once the change is committed. A statement sees the
    /// database as it was when the statement started, so the change is invisible to the held read and
    /// visible to the read after it. Batches without both reads are left alone.
    /// With <paramref name="streamVersionRead" /> it holds the stream version read instead, which comes before
    /// both, and acts on any batch with that read and an events read, so a write-cache hit, which reads no
    /// snapshot, is paused too.
    /// </summary>
    public sealed class PauseTheFirstRead(Func<Task> whilePaused, string snapshotTable = "mt_doc_simpleaggregate",
        int fetchIndex = 0, int pauses = 1, bool streamVersionRead = false): IMartenSessionLogger
    {
        private static long _nextKey = Random.Shared.Next(1, 1 << 30);
        private readonly List<Task> _pauses = new();

        /// <summary>How many batches carrying an Async fetch's reads have been sent.</summary>
        public int FetchBatches { get; private set; }

        /// <summary>Runs before each such batch is sent, on the caller's thread.</summary>
        public Action? BeforeEachFetchBatch { get; set; }

        /// <summary>Completes when every pause has run its change; fails if none was ever taken.</summary>
        public Task Paused => _pauses.Count == 0
            ? Task.FromException(new InvalidOperationException("No fetch was paused"))
            : Task.WhenAll(_pauses);

        public void OnBeforeExecute(NpgsqlBatch batch)
        {
            var commands = batch.BatchCommands.Cast<NpgsqlBatchCommand>().ToList();
            var snapshotReads = indexesOf(commands, isSnapshotRead);
            var eventsReads = indexesOf(commands, c => c.CommandText.Contains("mt_events as d"));
            var versionReads = indexesOf(commands, c => c.CommandText.Contains("mt_streams")
                                                        && !c.CommandText.Contains("mt_events"));
            var firstReads = streamVersionRead
                ? versionReads
                : snapshotReads.Zip(eventsReads, Math.Min).ToList();
            if (eventsReads.Count == 0 || (streamVersionRead ? versionReads.Count == 0 : snapshotReads.Count == 0)) return;

            FetchBatches++;
            BeforeEachFetchBatch?.Invoke();

            if (_pauses.Count >= pauses || fetchIndex >= firstReads.Count) return;

            var key = Interlocked.Increment(ref _nextKey);
            var gate = new NpgsqlConnection(ConnectionSource.ConnectionString);
            gate.Open();
            using (var take = gate.CreateCommand())
            {
                take.CommandText = $"select pg_advisory_lock({key})";
                take.ExecuteNonQuery();
            }

            var first = commands[firstReads[fetchIndex]];
            var where = first.CommandText.IndexOf(" where ", StringComparison.Ordinal) + " where ".Length;
            first.CommandText = first.CommandText.Insert(where,
                $"(select true from pg_advisory_xact_lock_shared({key})) and ");

            _pauses.Add(Task.Run(() => pauseAsync(gate, key)));
        }

        private async Task pauseAsync(NpgsqlConnection gate, long key)
        {
            try
            {
                await waitUntilTheReadIsHeldAsync(gate, key);
                await whilePaused();
            }
            finally
            {
                // Explicitly: a pooled connection keeps its session-level advisory locks when it is disposed
                await using (var release = gate.CreateCommand())
                {
                    release.CommandText = $"select pg_advisory_unlock({key})";
                    await release.ExecuteNonQueryAsync();
                }

                await gate.DisposeAsync();
            }
        }

        private static async Task waitUntilTheReadIsHeldAsync(NpgsqlConnection gate, long key)
        {
            await using var waiting = gate.CreateCommand();
            waiting.CommandText = $"select exists (select 1 from pg_locks where locktype = 'advisory' and not granted " +
                                  $"and objid::bigint = {key} and database = (select oid from pg_database where datname = current_database()))";

            var giveUp = DateTime.UtcNow.AddSeconds(30);
            while (!(bool)(await waiting.ExecuteScalarAsync())!)
            {
                if (DateTime.UtcNow > giveUp) throw new TimeoutException("The fetch's first read never reached the pause");
                await Task.Delay(10);
            }
        }

        private static List<int> indexesOf(List<NpgsqlBatchCommand> commands, Func<NpgsqlBatchCommand, bool> match) =>
            commands.Select((c, i) => (c, i)).Where(x => match(x.c)).Select(x => x.i).ToList();

        private bool isSnapshotRead(NpgsqlBatchCommand command) =>
            command.CommandText.Contains(snapshotTable) && command.CommandText.Contains("where id = ")
                                                        && !command.CommandText.Contains("mt_events");

        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlCommand command) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }

    public enum Fetch
    {
        ForWriting,
        ForWritingAtVersion,
        ForExclusiveWriting,
        Latest,
        StreamLatestJson,
        BatchedForWriting,
        BatchedForWritingAtVersion,
        BatchedLatest,
        ForWritingInsideCallerTransaction
    }

    public static IEnumerable<object[]> AllFetches() => Enum.GetValues<Fetch>().Select(x => new object[] { x });

    public static IEnumerable<object[]> UnbatchedFetches() =>
        Enum.GetValues<Fetch>().Where(x => !isBatched(x)).Select(x => new object[] { x });

    public static IEnumerable<object[]> BatchedFetches() =>
        Enum.GetValues<Fetch>().Where(isBatched).Select(x => new object[] { x });

    // The exclusive fetch's row lock makes an append wait until the fetch's session commits
    public static IEnumerable<object[]> FetchesAnAppendCanRace() =>
        Enum.GetValues<Fetch>().Where(x => x != Fetch.ForExclusiveWriting).Select(x => new object[] { x });

    private static bool isBatched(Fetch fetch) => fetch.ToString().StartsWith("Batched");

    /// <summary>What a fetch found, reduced to what every aggregate in these tests can report.</summary>
    public record struct Found(int A, int B, int C, int D, long? Version);

    private readonly StoreShape _shape;

    protected async_fetch_reads_in_a_safe_order(StoreShape shape)
    {
        _shape = shape;
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = false;
            shape.Configure(opts);
        });
    }

    private IDocumentSession sessionFor(string? tenant) =>
        tenant == null ? theStore.LightweightSession() : theStore.LightweightSession(tenant);

    private IDocumentSession session() => sessionFor(_shape.Tenant);

    private async Task catchTheDaemonUpAsync()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.CatchUpAsync(TestContext.Current.CancellationToken);
    }

    private async Task appendAsync(string? tenant, object id, bool start, params char[] letters)
    {
        await using var s = sessionFor(tenant);
        var events = letters.Select(_shape.Subject.Event).ToArray();
        if (start) _shape.Subject.Start(s, id, events);
        else _shape.Subject.Append(s, id, events);
        await s.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A stream of A, B, C. Where the shape has a second tenant, the same id there holds A and four B's
    /// and gets a snapshot of its own, so anything read from the wrong tenant shows in the counts.
    /// </summary>
    private async Task<object> aStreamWithNoSnapshotYetAsync()
    {
        var id = _shape.Subject.NewId();
        if (_shape.OtherTenant != null)
        {
            await appendAsync(_shape.OtherTenant, id, true, 'A', 'B', 'B', 'B', 'B');
            await catchTheDaemonUpAsync();
        }

        await appendAsync(_shape.Tenant, id, true, 'A', 'B', 'C');
        return id;
    }

    private async Task<object> aStreamWithAStaleSnapshotAsync()
    {
        var id = _shape.Subject.NewId();
        if (_shape.OtherTenant != null)
        {
            await appendAsync(_shape.OtherTenant, id, true, 'A', 'B', 'B', 'B', 'B');
        }

        await appendAsync(_shape.Tenant, id, true, 'A');
        await catchTheDaemonUpAsync();
        await appendAsync(_shape.Tenant, id, false, 'B', 'C');
        return id;
    }

    private async Task<object> aStreamWithACurrentSnapshotAsync()
    {
        var id = await aStreamWithNoSnapshotYetAsync();
        await catchTheDaemonUpAsync();
        return id;
    }

    private async Task deleteTheSnapshotAsync(object id)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"delete from {SchemaName}.{_shape.Subject.SnapshotTable} where id = @id";
        cmd.Parameters.AddWithValue("id", id);
        if (_shape.SnapshotIsTenanted)
        {
            cmd.CommandText += " and tenant_id = @tenant";
            cmd.Parameters.AddWithValue("tenant", _shape.Tenant!);
        }

        (await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Runs the fetch with <paramref name="interfere" /> committed between its two reads, and returns what
    /// it found. The batched fetches are enlisted behind a plain Load on purpose: an Async fetch should
    /// not care where in its batch it sits.
    /// </summary>
    private async Task<Found?> fetchWhileAsync(Fetch fetch, object id, Func<Task> interfere)
    {
        var ct = TestContext.Current.CancellationToken;

        NpgsqlConnection? conn = null;
        NpgsqlTransaction? tx = null;
        IDocumentSession s;
        if (fetch == Fetch.ForWritingInsideCallerTransaction)
        {
            conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync(ct);
            tx = await conn.BeginTransactionAsync(ct);
            var options = SessionOptions.ForTransaction(tx);
            if (_shape.Tenant != null) options.TenantId = _shape.Tenant;
            s = theStore.LightweightSession(options);
        }
        else
        {
            s = session();
        }

        try
        {
            var pause = new PauseTheFirstRead(interfere, _shape.Subject.SnapshotTable);
            s.Logger = pause;

            var found = await _shape.Subject.FetchAsync(theStore, s, fetch, id, 3, ct);
            await pause.Paused;

            if (tx != null) await tx.RollbackAsync(ct);
            return found;
        }
        finally
        {
            await s.DisposeAsync();
            if (conn != null) await conn.DisposeAsync();
        }
    }

    private static void shouldBe(Found? found, int a, int b, int c, int d, long version)
    {
        found.ShouldNotBeNull();
        found.Value.A.ShouldBe(a);
        found.Value.B.ShouldBe(b);
        found.Value.C.ShouldBe(c);
        found.Value.D.ShouldBe(d);
        if (found.Value.Version.HasValue) found.Value.Version.Value.ShouldBe(version);
    }

    private static void shouldBeTheWholeStream(Found? found) => shouldBe(found, 1, 1, 1, 0, 3);

    [Theory]
    [MemberData(nameof(AllFetches))]
    public async Task the_daemon_writes_the_first_snapshot_mid_fetch(Fetch fetch)
    {
        var id = await aStreamWithNoSnapshotYetAsync();

        shouldBeTheWholeStream(await fetchWhileAsync(fetch, id, catchTheDaemonUpAsync));
    }

    [Theory]
    [MemberData(nameof(AllFetches))]
    public async Task the_daemon_advances_a_stale_snapshot_mid_fetch(Fetch fetch)
    {
        var id = await aStreamWithAStaleSnapshotAsync();

        shouldBeTheWholeStream(await fetchWhileAsync(fetch, id, catchTheDaemonUpAsync));
    }

    // A rebuild deletes snapshots before it writes them again, so a snapshot can go BACKWARDS between the
    // two reads. Then the events read skipped events the snapshot read no longer has. That is detected and
    // redone where a redo is possible, and refused with SnapshotRaceException in a batch, where it is not.

    [Theory]
    [MemberData(nameof(UnbatchedFetches))]
    public async Task a_current_snapshot_is_deleted_mid_fetch(Fetch fetch)
    {
        var id = await aStreamWithACurrentSnapshotAsync();

        shouldBeTheWholeStream(await fetchWhileAsync(fetch, id, () => deleteTheSnapshotAsync(id)));
    }

    [Theory]
    [MemberData(nameof(UnbatchedFetches))]
    public async Task a_stale_snapshot_is_deleted_mid_fetch(Fetch fetch)
    {
        var id = await aStreamWithAStaleSnapshotAsync();

        shouldBeTheWholeStream(await fetchWhileAsync(fetch, id, () => deleteTheSnapshotAsync(id)));
    }

    [Theory]
    [MemberData(nameof(BatchedFetches))]
    public async Task a_batch_refuses_a_snapshot_deleted_mid_fetch(Fetch fetch)
    {
        var id = await aStreamWithAStaleSnapshotAsync();

        var ex = await Should.ThrowAsync<SnapshotRaceException>(() =>
            fetchWhileAsync(fetch, id, () => deleteTheSnapshotAsync(id)));
        ex.Id.ShouldBe(id);
    }

    // An event appended and projected mid-fetch: the stream version was read at 3, the events read stopped
    // at 3, and the snapshot read finds 4. The fetch answers at 4, aggregate and version both, because that
    // is the newest state any of its reads saw.

    private async Task appendAnEventAndCatchTheDaemonUpAsync(object id)
    {
        await appendAsync(_shape.Tenant, id, false, 'D');
        await catchTheDaemonUpAsync();
    }

    [Theory]
    [MemberData(nameof(FetchesAnAppendCanRace))]
    public async Task an_event_is_appended_and_projected_mid_fetch(Fetch fetch)
    {
        var id = await aStreamWithACurrentSnapshotAsync();

        var expectingVersion3 = fetch is Fetch.ForWritingAtVersion or Fetch.BatchedForWritingAtVersion;
        if (expectingVersion3)
        {
            // Expected 3, and the stream reached 4 while the fetch was reading it
            var ex = await Should.ThrowAsync<ConcurrencyException>(() =>
                fetchWhileAsync(fetch, id, () => appendAnEventAndCatchTheDaemonUpAsync(id)));
            ex.Message.ShouldContain("but was 4");
            return;
        }

        shouldBe(await fetchWhileAsync(fetch, id, () => appendAnEventAndCatchTheDaemonUpAsync(id)), 1, 1, 1, 1, 4);
    }

    // A stream that does not exist yet when the fetch reads its version, and is started and projected before
    // the snapshot read. Its snapshot is real, so the fetch answers with it.

    [Theory]
    [MemberData(nameof(AllFetches))]
    public async Task a_stream_is_started_and_projected_mid_fetch(Fetch fetch)
    {
        var id = _shape.Subject.NewId();
        if (_shape.OtherTenant != null)
        {
            await appendAsync(_shape.OtherTenant, id, true, 'A', 'B', 'B', 'B', 'B');
            await catchTheDaemonUpAsync();
        }

        shouldBeTheWholeStream(await fetchWhileAsync(fetch, id, async () =>
        {
            await appendAsync(_shape.Tenant, id, true, 'A', 'B', 'C');
            await catchTheDaemonUpAsync();
        }));
    }

    // A snapshot whose stream is gone, as DeleteAllEventDataAsync leaves it: every fetch refuses it, batched
    // or not, rather than answer with the snapshot or with a new, empty aggregate.

    private async Task deleteTheStreamAsync(object id)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"delete from {SchemaName}.mt_events where stream_id = @id; " +
                          $"delete from {SchemaName}.mt_streams where id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(AllFetches))]
    public async Task a_snapshot_that_outlived_its_stream_is_refused(Fetch fetch)
    {
        var id = await aStreamWithACurrentSnapshotAsync();
        await deleteTheStreamAsync(id);

        var ex = await Should.ThrowAsync<SnapshotWithoutStreamException>(() => fetchWhileAsync(fetch, id, () => Task.CompletedTask));
        ex.Id.ShouldBe(id);
    }
}

/// <summary>
/// One store setup for <see cref="async_fetch_reads_in_a_safe_order" />: how the store is configured, the
/// aggregate it fetches, and the tenant the fetch runs in.
/// </summary>
public sealed record StoreShape(
    Action<StoreOptions> Configure,
    FetchSubject Subject,
    string? Tenant = null,
    string? OtherTenant = null,
    bool SnapshotIsTenanted = false);

/// <summary>Drives one aggregate type through every kind of fetch, by Guid or string stream identity.</summary>
public abstract class FetchSubject
{
    public abstract string SnapshotTable { get; }
    public abstract object NewId();
    public abstract object Event(char letter);
    public abstract void Start(IDocumentSession session, object id, object[] events);
    public abstract void Append(IDocumentSession session, object id, object[] events);

    public abstract Task<async_fetch_reads_in_a_safe_order.Found?> FetchAsync(IDocumentStore store,
        IDocumentSession session, async_fetch_reads_in_a_safe_order.Fetch fetch, object id, long expectedVersion,
        CancellationToken ct);
}

public sealed class FetchSubject<T>(
    string snapshotTable,
    Func<object> newId,
    Func<char, object> eventFor,
    Func<T, async_fetch_reads_in_a_safe_order.Found> countsOf): FetchSubject where T : class
{
    public override string SnapshotTable => snapshotTable;
    public override object NewId() => newId();
    public override object Event(char letter) => eventFor(letter);

    public override void Start(IDocumentSession session, object id, object[] events)
    {
        if (id is Guid guid) session.Events.StartStream<T>(guid, events);
        else session.Events.StartStream<T>((string)id, events);
    }

    public override void Append(IDocumentSession session, object id, object[] events)
    {
        if (id is Guid guid) session.Events.Append(guid, events);
        else session.Events.Append((string)id, events);
    }

    private async_fetch_reads_in_a_safe_order.Found? found(T? aggregate, long? version) =>
        aggregate == null ? null : countsOf(aggregate) with { Version = version };

    private async_fetch_reads_in_a_safe_order.Found? found(IEventStream<T> stream) =>
        found(stream.Aggregate, stream.CurrentVersion);

    public override async Task<async_fetch_reads_in_a_safe_order.Found?> FetchAsync(IDocumentStore store,
        IDocumentSession session, async_fetch_reads_in_a_safe_order.Fetch fetch, object id, long expectedVersion,
        CancellationToken ct)
    {
        var events = session.Events;
        var guid = id as Guid?;
        var key = id as string;

        switch (fetch)
        {
            case async_fetch_reads_in_a_safe_order.Fetch.ForWriting:
            case async_fetch_reads_in_a_safe_order.Fetch.ForWritingInsideCallerTransaction:
                return found(guid.HasValue
                    ? await events.FetchForWriting<T>(guid.Value, ct)
                    : await events.FetchForWriting<T>(key!, ct));

            case async_fetch_reads_in_a_safe_order.Fetch.ForWritingAtVersion:
                return found(guid.HasValue
                    ? await events.FetchForWriting<T>(guid.Value, expectedVersion, ct)
                    : await events.FetchForWriting<T>(key!, expectedVersion, ct));

            case async_fetch_reads_in_a_safe_order.Fetch.ForExclusiveWriting:
                return found(guid.HasValue
                    ? await events.FetchForExclusiveWriting<T>(guid.Value, ct)
                    : await events.FetchForExclusiveWriting<T>(key!, ct));

            case async_fetch_reads_in_a_safe_order.Fetch.Latest:
                return found(guid.HasValue
                    ? await events.FetchLatest<T>(guid.Value, ct)
                    : await events.FetchLatest<T>(key!, ct), null);

            case async_fetch_reads_in_a_safe_order.Fetch.StreamLatestJson:
                using (var json = new MemoryStream())
                {
                    var any = guid.HasValue
                        ? await events.StreamLatestJson<T>(guid.Value, json, ct)
                        : await events.StreamLatestJson<T>(key!, json, ct);
                    if (!any) return null;

                    json.Position = 0;
                    return found(store.Options.Serializer().FromJson<T>(json), null);
                }

            default:
                return await batchedAsync(session, fetch, guid, key, expectedVersion, ct);
        }
    }

    private async Task<async_fetch_reads_in_a_safe_order.Found?> batchedAsync(IDocumentSession session,
        async_fetch_reads_in_a_safe_order.Fetch fetch, Guid? guid, string? key, long expectedVersion,
        CancellationToken ct)
    {
        var batch = session.CreateBatchQuery();
        var before = batch.Load<Target>(Guid.NewGuid());

        Task<async_fetch_reads_in_a_safe_order.Found?> query = fetch switch
        {
            async_fetch_reads_in_a_safe_order.Fetch.BatchedForWriting => streamed(guid.HasValue
                ? batch.Events.FetchForWriting<T>(guid.Value)
                : batch.Events.FetchForWriting<T>(key!)),
            async_fetch_reads_in_a_safe_order.Fetch.BatchedForWritingAtVersion => streamed(guid.HasValue
                ? batch.Events.FetchForWriting<T>(guid.Value, expectedVersion)
                : batch.Events.FetchForWriting<T>(key!, expectedVersion)),
            async_fetch_reads_in_a_safe_order.Fetch.BatchedLatest => latest(guid.HasValue
                ? batch.Events.FetchLatest<T>(guid.Value)
                : batch.Events.FetchLatest<T>(key!)),
            _ => throw new ArgumentOutOfRangeException(nameof(fetch))
        };

        await batch.Execute(ct);
        (await before).ShouldBeNull();
        return await query;
    }

    private async Task<async_fetch_reads_in_a_safe_order.Found?> streamed(Task<IEventStream<T>> fetch) =>
        found(await fetch);

    private async Task<async_fetch_reads_in_a_safe_order.Found?> latest(Task<T?> fetch) =>
        found(await fetch, null);
}

internal static class StoreShapes
{
    private static object simpleEvent(char letter) => letter switch
    {
        'A' => new AEvent(),
        'B' => new BEvent(),
        'C' => new CEvent(),
        'D' => new DEvent(),
        _ => throw new ArgumentOutOfRangeException(nameof(letter))
    };

    private static object specialEvent(char letter) => letter switch
    {
        'A' => new SpecialA(),
        'B' => new SpecialB(),
        'C' => new SpecialC(),
        'D' => new SpecialD(),
        _ => throw new ArgumentOutOfRangeException(nameof(letter))
    };

    public static readonly FetchSubject SimpleAggregate = new FetchSubject<SimpleAggregate>(
        "mt_doc_simpleaggregate", () => Guid.NewGuid(), simpleEvent,
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));

    public static readonly FetchSubject SimpleAggregateAsString = new FetchSubject<SimpleAggregateAsString>(
        "mt_doc_simpleaggregateasstring", () => Guid.NewGuid().ToString(), simpleEvent,
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));

    public static readonly FetchSubject SpecialCounter = new FetchSubject<SpecialCounter>(
        "mt_doc_specialcounter", () => Guid.NewGuid(), specialEvent,
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));

    public static readonly FetchSubject StrongTypedAggregate = new FetchSubject<StrongTypedAggregate>(
        "mt_doc_strongtypedaggregate", () => Guid.NewGuid(), simpleEvent,
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));
}

public readonly record struct StrongTypedAggregateId(Guid Value);

/// <summary><see cref="SimpleAggregate" /> with a strong-typed id, fetched by its Guid stream id.</summary>
public class StrongTypedAggregate: IRevisioned
{
    public int Version { get; set; }
    public StrongTypedAggregateId Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    public void Apply(AEvent _) => ACount++;
    public void Apply(BEvent _) => BCount++;
    public void Apply(CEvent _) => CCount++;
    public void Apply(DEvent _) => DCount++;
}

public class async_fetch_reads_in_a_safe_order_on_guid_streams(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async),
    StoreShapes.SimpleAggregate));

public class async_fetch_reads_in_a_safe_order_on_string_streams(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts =>
    {
        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Async);
    },
    StoreShapes.SimpleAggregateAsString));

public class async_fetch_reads_in_a_safe_order_with_conjoined_tenancy(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts =>
    {
        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
    },
    StoreShapes.SimpleAggregate, Tenant: "tenant-a", OtherTenant: "tenant-b", SnapshotIsTenanted: true));

public class async_fetch_reads_in_a_safe_order_on_a_global_aggregate_with_conjoined_tenancy():
    async_fetch_reads_in_a_safe_order(new StoreShape(
        opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), ProjectionLifecycle.Async);
        },
        StoreShapes.SpecialCounter, Tenant: "tenant-a"));

public class async_fetch_reads_in_a_safe_order_with_the_identity_map(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts =>
    {
        opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        opts.Events.UseIdentityMapForAggregates = true;
    },
    StoreShapes.SimpleAggregate));

public class async_fetch_reads_in_a_safe_order_with_the_write_cache(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts =>
    {
        opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
    },
    StoreShapes.SimpleAggregate));

public class async_fetch_reads_in_a_safe_order_with_a_strong_typed_id(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts =>
    {
        opts.RegisterValueType<StrongTypedAggregateId>();
        opts.Projections.Snapshot<StrongTypedAggregate>(SnapshotLifecycle.Async);
    },
    StoreShapes.StrongTypedAggregate));
