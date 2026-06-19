using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #4765 (root cause of #4749) — under <see cref="EventAppendMode.Quick"/> /
/// <see cref="EventAppendMode.QuickWithServerTimestamps"/> on the
/// non-partitioned event store, an optimistic-concurrency loser
/// (FetchForWriting / AppendOptimistic / AppendExclusive / expected-version
/// StartStream) used to leave a permanent gap in <c>mt_events_sequence</c>:
/// the per-event INSERT fired <c>nextval()</c> (non-transactional) before the
/// 23505 raised and rolled the txn back, so the sequence stayed advanced past
/// the highest committed seq_id. The async daemon's high-water detector then
/// stalled on the unreachable value forever.
///
/// The fix routes every Quick append through <c>mt_quick_append_events</c>,
/// which checks the version (MT003) BEFORE any nextval — so an OCC loss
/// advances no sequence. These tests pin the no-gap invariant, the unchanged
/// concurrency-exception contract, and the (now consistent) timestamp behavior.
/// </summary>
public class Bug_4765_quick_occ_no_sequence_gap: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4765_quick_occ_no_sequence_gap(ITestOutputHelper output)
    {
        _output = output;
    }

    private async Task<DocumentStore> BuildStoreAsync(
        string label, EventAppendMode mode, bool asyncSnapshot, TimeProvider timeProvider = null)
    {
        // Keep schema names well under PostgreSQL's 63-char identifier limit —
        // a too-long name gets silently truncated by PG, which then trips schema
        // validation (config name != truncated actual name).
        var modeCode = mode == EventAppendMode.Quick ? "q" : "qst";
        var schema = $"b4765_{label}_{modeCode}";

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"drop schema if exists {schema} cascade").ExecuteNonQueryAsync();
        }

        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.AppendMode = mode;
            if (timeProvider != null)
            {
                opts.Events.TimeProvider = timeProvider;
            }

            if (asyncSnapshot)
            {
                opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            }
        });

        _disposables.Add(store);
        return store;
    }

    private static async Task<(long lastValue, long maxSeq, long rowCount)> ReadSequenceStateAsync(DocumentStore store)
    {
        var schema = store.Options.Events.DatabaseSchemaName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var lastValue = Convert.ToInt64(
            await conn.CreateCommand($"select last_value from {schema}.mt_events_sequence").ExecuteScalarAsync());

        var maxObj = await conn.CreateCommand($"select max(seq_id) from {schema}.mt_events").ExecuteScalarAsync();
        var maxSeq = maxObj is null or DBNull ? 0L : Convert.ToInt64(maxObj);

        var rowCount = Convert.ToInt64(
            await conn.CreateCommand($"select count(*) from {schema}.mt_events").ExecuteScalarAsync());

        return (lastValue, maxSeq, rowCount);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task occ_loser_leaves_no_sequence_gap(EventAppendMode mode)
    {
        using var store = await BuildStoreAsync("gap", mode, asyncSnapshot: false);

        var streamId = Guid.NewGuid();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
            await seed.SaveChangesAsync();
        }

        // Two sessions fetch the same stream at the same version, then race to append.
        await using var s1 = store.LightweightSession();
        await using var s2 = store.LightweightSession();

        var w1 = await s1.Events.FetchForWriting<SimpleAggregate>(streamId);
        var w2 = await s2.Events.FetchForWriting<SimpleAggregate>(streamId);

        w1.AppendOne(new CEvent());
        w2.AppendMany(new DEvent(), new EEvent());

        // Winner commits.
        await s1.SaveChangesAsync();

        // Loser must throw a concurrency exception...
        var ex = await Record.ExceptionAsync(() => s2.SaveChangesAsync());
        _output.WriteLine(ex?.GetType().FullName ?? "<no exception>");
        ex.ShouldNotBeNull();
        ex.ShouldBeAssignableTo<ConcurrencyException>();

        // ...and must NOT have advanced the sequence (no nextval before the version
        // check). This assertion FAILS on master for the non-partitioned Quick path.
        var (lastValue, maxSeq, rowCount) = await ReadSequenceStateAsync(store);
        lastValue.ShouldBe(maxSeq);

        // 2 seed + 1 winner; the loser's events rolled back entirely.
        rowCount.ShouldBe(3);

        // No tombstones leaked from the failure.
        var schema = store.Options.Events.DatabaseSchemaName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var tombstones = Convert.ToInt64(await conn
            .CreateCommand($"select count(*) from {schema}.mt_events where type = 'tombstone'")
            .ExecuteScalarAsync());
        tombstones.ShouldBe(0);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task async_daemon_recovers_after_occ_loss(EventAppendMode mode)
    {
        using var store = await BuildStoreAsync("daemon", mode, asyncSnapshot: true);

        var streamId = Guid.NewGuid();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
            await seed.SaveChangesAsync();
        }

        // Force a concurrency failure to exercise the (previously gap-leaving) path.
        await using (var s1 = store.LightweightSession())
        await using (var s2 = store.LightweightSession())
        {
            var w1 = await s1.Events.FetchForWriting<SimpleAggregate>(streamId);
            var w2 = await s2.Events.FetchForWriting<SimpleAggregate>(streamId);
            w1.AppendOne(new CEvent());
            w2.AppendOne(new DEvent());
            await s1.SaveChangesAsync();
            await Record.ExceptionAsync(() => s2.SaveChangesAsync());
        }

        // With no sequence gap, the async daemon catches up to the real high-water
        // promptly instead of stalling forever on an unreachable ceiling (#4749).
        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(15.Seconds());

        await using var query = store.QuerySession();
        var aggregate = await query.LoadAsync<SimpleAggregate>(streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1); // winner's event
        aggregate.DCount.ShouldBe(0); // loser rolled back
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task quick_append_function_is_idempotent_with_expected_version(EventAppendMode mode)
    {
        // The mt_quick_append_events function now always carries the trailing
        // `expected_version` parameter. PostgreSQL canonicalizes a declared `int`
        // parameter to `integer` and a bare `DEFAULT NULL` to `DEFAULT NULL::<type>`
        // in pg_get_functiondef, which Weasel's function-diff reads back. If the
        // generated SQL doesn't already match that rendering, schema validation
        // reports a perpetual delta. This pins idempotency on the common
        // non-bigint (integer) Quick path.
        using var store = await BuildStoreAsync("idem", mode, asyncSnapshot: false);

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(JasperFx.Events.IEvent), default);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Must not detect any further changes (would throw DatabaseValidationException).
        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    private sealed class FixedTimeProvider: TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task concurrent_occ_appends_to_same_stream_leave_no_sequence_gap(EventAppendMode mode)
    {
        // Regression for the race NOT covered by occ_loser_leaves_no_sequence_gap:
        // that test is sequential (s1 commits, then s2 runs). Here ALL three sessions
        // fetch the same stream version BEFORE any of them commits, then all three
        // SaveChanges calls are released at exactly the same moment via a Barrier on
        // dedicated threads. Under READ COMMITTED, two losers can both pass the
        // mt_quick_append_events version SELECT before the winner commits, reach
        // nextval(), and fail on the mt_events PK (23505). nextval is non-transactional
        // so the sequence stays advanced, stalling the async daemon.
        // With FOR UPDATE on the version SELECT, losers block at the SELECT until the
        // winner commits, then read the updated version and raise MT003 before any
        // nextval — no gap.
        using var store = await BuildStoreAsync("cc_gap", mode, asyncSnapshot: false);

        var streamId = Guid.NewGuid();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
            await seed.SaveChangesAsync();
        }

        // All three sessions fetch the same version BEFORE any commits.
        await using var s1 = store.LightweightSession();
        await using var s2 = store.LightweightSession();
        await using var s3 = store.LightweightSession();

        var w1 = await s1.Events.FetchForWriting<SimpleAggregate>(streamId);
        var w2 = await s2.Events.FetchForWriting<SimpleAggregate>(streamId);
        var w3 = await s3.Events.FetchForWriting<SimpleAggregate>(streamId);

        w1.AppendOne(new CEvent());
        w2.AppendOne(new DEvent());
        w3.AppendOne(new EEvent());

        // Use a Barrier to release all three SaveChanges calls at exactly the same
        // instant from dedicated OS threads — this maximises the chance that all three
        // PG connections are inside mt_quick_append_events simultaneously, triggering
        // the TOCTOU race between the version SELECT and the nextval/INSERT.
        var barrier = new System.Threading.Barrier(participantCount: 3);
        var exceptions = new Exception?[3];

        void SaveSync(int idx, IDocumentSession session)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            { session.SaveChangesAsync().GetAwaiter().GetResult(); }
            catch (Exception e) { exceptions[idx] = e; }
        }

        var threads = new[]
        {
            new System.Threading.Thread(() => SaveSync(0, s1)),
            new System.Threading.Thread(() => SaveSync(1, s2)),
            new System.Threading.Thread(() => SaveSync(2, s3)),
        };

        foreach (var t in threads)
            t.Start();
        foreach (var t in threads)
            t.Join();

        var successes = Array.FindAll(exceptions, static e => e is null).Length;
        var failures = Array.FindAll(exceptions, static e => e is ConcurrencyException).Length;
        _output.WriteLine($"successes={successes}, concurrency failures={failures}");
        for (var i = 0; i < exceptions.Length; i++)
            if (exceptions[i] != null)
                _output.WriteLine($"  thread[{i}]: {exceptions[i]!.GetType().Name} — {exceptions[i]!.Message}");

        successes.ShouldBe(1, "exactly one concurrent writer should win");
        failures.ShouldBe(2, "the other two should get ConcurrencyException");

        // The critical assertion: sequence must equal the highest committed seq_id.
        // A gap means nextval() fired for a losing transaction — the daemon stalls.
        var (lastValue, maxSeq, rowCount) = await ReadSequenceStateAsync(store);
        _output.WriteLine($"last_value={lastValue}, max seq_id={maxSeq}, rows={rowCount}");
        lastValue.ShouldBe(maxSeq, $"sequence gap: last_value={lastValue} > max committed seq_id={maxSeq}");

        // 2 seed + 1 winner; both losers must have rolled back cleanly.
        rowCount.ShouldBe(3);
    }

    [Fact]
    public async Task quick_mode_uses_server_timestamp_on_optimistic_path()
    {
        // EventAppendMode.Quick contracts "timestamps from the database server".
        // The old per-event OCC path bound the client IEvent.Timestamp regardless —
        // a latent inconsistency the consolidation onto mt_quick_append_events fixes.
        var staleClientTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var store = await BuildStoreAsync(
            "tssrv", EventAppendMode.Quick, asyncSnapshot: false, timeProvider: new FixedTimeProvider(staleClientTime));

        var streamId = Guid.NewGuid();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
            await seed.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.AppendOne(new BEvent());
            await session.SaveChangesAsync();
        }

        var persisted = await ReadLatestTimestampAsync(store, streamId);
        // Server time, not the stale 2000 client time.
        persisted.Year.ShouldBe(DateTimeOffset.UtcNow.Year);
    }

    [Fact]
    public async Task quick_with_server_timestamps_uses_client_timestamp_on_optimistic_path()
    {
        // EventAppendMode.QuickWithServerTimestamps contracts "timestamps from the
        // .NET TimeProvider" — the bulk function binds the client-supplied array.
        var clientTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var store = await BuildStoreAsync(
            "tscli", EventAppendMode.QuickWithServerTimestamps, asyncSnapshot: false,
            timeProvider: new FixedTimeProvider(clientTime));

        var streamId = Guid.NewGuid();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
            await seed.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.AppendOne(new BEvent());
            await session.SaveChangesAsync();
        }

        var persisted = await ReadLatestTimestampAsync(store, streamId);
        persisted.Year.ShouldBe(2000);
    }

    private static async Task<DateTimeOffset> ReadLatestTimestampAsync(DocumentStore store, Guid streamId)
    {
        var schema = store.Options.Events.DatabaseSchemaName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var value = await conn
            .CreateCommand($"select timestamp from {schema}.mt_events where stream_id = :id order by version desc limit 1")
            .With("id", streamId, NpgsqlDbType.Uuid)
            .ExecuteScalarAsync();

        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"Unexpected timestamp value: {value}")
        };
    }
}
