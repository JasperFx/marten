using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Bugs;

/// <summary>
/// Regressions for discussion #4953: the async daemon's high water detection must never advance
/// across an OUTSTANDING sequence number — one reserved by a transaction that is still in flight
/// and will commit later. Four mechanisms could do exactly that (see the issue): per-statement
/// snapshot skew inside the GapDetector command, threshold-free safe-zone skips from
/// rebuild/catch-up, the "-32" stale fallback teleporting into reserved-but-uncommitted sequence
/// numbers, and the wall-clock stale skip crossing transactions that are merely slow.
/// </summary>
public class Bug_4953_outstanding_sequence_gap_skips: DaemonContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4953_outstanding_sequence_gap_skips(ITestOutputHelper output): base(output)
    {
        _output = output;
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    #region helpers

    private async Task<NpgsqlConnection> openConnection()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task appendEvents(int count, Guid? streamId = null)
    {
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < count; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(streamId ?? Guid.NewGuid(), i + 1));
        }

        await session.SaveChangesAsync();
    }

    private async Task<long> scalar(string sql)
    {
        await using var conn = await openConnection();
        var raw = await conn.CreateCommand(sql).ExecuteScalarAsync();
        return raw is long l ? l : Convert.ToInt64(raw ?? 0L);
    }

    private async Task execute(string sql)
    {
        await using var conn = await openConnection();
        await conn.CreateCommand(sql).ExecuteNonQueryAsync();
    }

    // A faithful stand-in for a slow concurrent append: reserves the next sequence number and
    // inserts its event row WITHOUT committing, exactly like an in-flight SaveChanges.
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx, long seq)> startOutstandingAppend()
    {
        var conn = await openConnection();
        var tx = await conn.BeginTransactionAsync();
        var seq = (long)(await conn.CreateCommand($"select nextval('{Schema}.mt_events_sequence')")
            .ExecuteScalarAsync())!;
        await conn.CreateCommand($@"
insert into {Schema}.mt_events(seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type, is_archived)
select {seq}, gen_random_uuid(), stream_id, 100000 + {seq}, data, type, now(), tenant_id, mt_dotnet_type, false
from {Schema}.mt_events where seq_id = 1").ExecuteNonQueryAsync();
        return (conn, tx, seq);
    }

    private HighWaterDetector buildDetector()
    {
        return new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            NullLogger.Instance);
    }

    private static int countStatements(string commandText)
    {
        return commandText.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Count(s => !string.IsNullOrWhiteSpace(s));
    }

    #endregion

    // ---------------------------------------------------------------------------------------------
    // Mechanism 1: the GapDetector command batches multiple statements, and under READ COMMITTED
    // every statement gets its OWN snapshot. Commits landing between the leading-gap probe, the
    // interior-gap query, and the max() fallback can defeat all gap checks and let the NORMAL
    // (silent) path cross an outstanding sequence. The only structural guarantee against snapshot
    // skew is a single statement.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void gap_detector_command_is_a_single_statement_for_a_single_snapshot()
    {
        StoreOptions(opts => { });
        theStore.EnsureStorageExists(typeof(IEvent));

        var command = new GapDetector(theStore.Events).BuildCommand();
        countStatements(command.CommandText).ShouldBe(1);
    }

    [Fact]
    public void statistics_detector_command_is_a_single_statement_for_a_single_snapshot()
    {
        StoreOptions(opts => { });
        theStore.EnsureStorageExists(typeof(IEvent));

        var command = new HighWaterStatisticsDetector(theStore.Events).BuildCommand();
        countStatements(command.CommandText).ShouldBe(1);
    }

    // Static pin (already green): the Normal path must hold before a leading gap held by an open
    // transaction. Guards the #4964 fix while the command shape changes.
    [Fact]
    public async Task normal_detection_holds_before_an_open_transaction_gap()
    {
        StoreOptions(opts => { });
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        var (conn, tx, seq) = await startOutstandingAppend();
        try
        {
            seq.ShouldBe(9);
            await appendEvents(3); // 10..12 committed

            var statistics = await buildDetector().Detect(CancellationToken.None);
            statistics.CurrentMark.ShouldBe(8);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Mechanisms 2 + 4: DetectInSafeZone is invoked with no staleness gating by rebuilds and
    // catch-up (CheckNowAsync), and by the poll loop after StaleSequenceThreshold. In both cases it
    // must NOT cross a sequence whose reserving transaction is demonstrably still alive.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task safe_zone_detection_holds_while_the_reserving_transaction_is_alive()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        var (conn, tx, seq) = await startOutstandingAppend(); // seq 9, held open
        try
        {
            seq.ShouldBe(9);
            await appendEvents(3); // 10..12 committed

            var detector = buildDetector();

            // First sighting of the gap — must hold regardless (threshold not elapsed)
            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8);

            // Well past the stale threshold — must STILL hold, because the reserving transaction
            // is provably alive (RowExclusiveLock on mt_events + open transaction evidence)
            await Task.Delay(700);
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"After threshold with live reserver: CurrentMark={second.CurrentMark}");
            second.CurrentMark.ShouldBe(8);

            var persisted = await scalar(
                $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
            persisted.ShouldBe(8);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    [Fact]
    public async Task safe_zone_detection_skips_a_dead_gap_after_the_threshold_and_records_it()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
            opts.Events.EnableAdvancedAsyncTracking = true;
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        var (conn, tx, seq) = await startOutstandingAppend(); // seq 9
        long persistedMark;
        try
        {
            seq.ShouldBe(9);
            await appendEvents(3); // 10..12 committed
            await tx.RollbackAsync(); // seq 9 is now a permanently dead hole

            var detector = buildDetector();

            // First sighting — hold (threshold measured from first observation of THIS gap)
            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8);

            await Task.Delay(700);

            // Threshold elapsed + no live reserver = provably dead: skip, and record the skip
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            second.CurrentMark.ShouldBe(12);
            second.IncludesSkipping.ShouldBeTrue();

            persistedMark = await scalar(
                $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
        }
        finally
        {
            await conn.DisposeAsync();
        }

        persistedMark.ShouldBe(12);
        var skips = await scalar($"select count(*) from {Schema}.mt_high_water_skips");
        skips.ShouldBeGreaterThan(0);
    }

    // ---------------------------------------------------------------------------------------------
    // Mechanism 3: idle store, then a sudden crush reserves thousands of sequence numbers before
    // the first commits land. The stale fallback must not teleport to (reserved last_value - 32):
    // it must hold while reservers live, and once the tail is provably dead it may advance to the
    // reserved ceiling recorded when the gap was first observed — all of it, no magic -32.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task stale_fallback_holds_for_live_reservations_then_skips_to_the_observed_ceiling()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");
        // the store has been idle for an hour — last_updated must NOT satisfy the stale gate
        await execute(
            $"update {Schema}.mt_event_progression set last_updated = now() - interval '1 hour' where name = 'HighWaterMark'");

        var (conn, tx, seq) = await startOutstandingAppend(); // seq 9, in flight
        try
        {
            seq.ShouldBe(9);
            // the crush: 5000 more reservations, none committed yet
            await execute($"select nextval('{Schema}.mt_events_sequence') from generate_series(1, 5000)");
            var lastValue = await scalar($"select last_value from {Schema}.mt_events_sequence");

            var detector = buildDetector();

            // Despite the ancient last_updated, the first sighting of this gap must hold
            var first = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"First sighting: CurrentMark={first.CurrentMark}");
            first.CurrentMark.ShouldBe(8);

            // Still holding while the reserving transaction lives, even past the threshold
            await Task.Delay(700);
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            second.CurrentMark.ShouldBe(8);

            // Kill the whole tail: every reservation is now provably dead
            await tx.RollbackAsync();
            await Task.Delay(700);

            var third = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"After tail death: CurrentMark={third.CurrentMark} (ceiling {lastValue})");
            third.CurrentMark.ShouldBe(lastValue);
            third.IncludesSkipping.ShouldBeTrue();
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Mechanism 2 end-to-end: a projection rebuild kicked off while an append is in flight must
    // wait for that append instead of silently skipping it.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task rebuild_during_an_inflight_append_waits_instead_of_skipping()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new Bug4953GapViewProjection(), ProjectionLifecycle.Async);
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var viewId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < 8; i++)
            {
                session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(viewId, i + 1));
            }

            await session.SaveChangesAsync();
        }

        var (conn, tx, seq) = await startOutstandingAppend(); // seq 9 in flight
        try
        {
            seq.ShouldBe(9);

            await using (var session = theStore.LightweightSession())
            {
                for (var i = 0; i < 3; i++)
                {
                    session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(viewId, 10 + i));
                }

                await session.SaveChangesAsync(); // seqs 10..12 committed
            }

            using var daemon = await StartDaemon();

            // release the in-flight append while the rebuild is waiting on the high water mark
            var release = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await tx.CommitAsync();
            });

            await daemon.RebuildProjectionAsync<Bug4953GapView>(CancellationToken.None);
            await release;

            await daemon.WaitForNonStaleData(30.Seconds());

            var persisted = await scalar($"select count(*) from {Schema}.mt_events");
            await using var query = theStore.QuerySession();
            var views = await query.Query<Bug4953GapView>().ToListAsync();
            var projectedSequences = views.SelectMany(x => x.Sequences).OrderBy(x => x).ToArray();

            _output.WriteLine($"persisted={persisted}, projected=[{string.Join(",", projectedSequences)}]");
            projectedSequences.Length.ShouldBe((int)persisted);
            projectedSequences.ShouldContain(9);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Mechanism 4 end-to-end (the reporter's trigger technique): the running daemon's stale skip
    // must not cross an append transaction that is merely slow — the trigger holds the FIRST event
    // of the store in flight past the stale threshold while later events commit.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task running_daemon_holds_for_a_slow_append_instead_of_stale_skipping_it()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 1.Seconds();
            opts.Projections.Add(new Bug4953GapViewProjection(), ProjectionLifecycle.Async);
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var viewId = Guid.NewGuid();

        await execute($$"""
                      CREATE OR REPLACE FUNCTION {{Schema}}.bug4953_sleep_insert() RETURNS trigger LANGUAGE plpgsql AS $function$
                      BEGIN
                          PERFORM pg_sleep(4);
                          RETURN NEW;
                      END;
                      $function$;

                      DROP TRIGGER IF EXISTS bug4953_sleep_insert ON {{Schema}}.mt_events;

                      CREATE TRIGGER bug4953_sleep_insert
                      AFTER INSERT ON {{Schema}}.mt_events
                      FOR EACH ROW
                      WHEN (NEW.data ->> 'Number' = '0' OR NEW.data ->> 'number' = '0')
                      EXECUTE FUNCTION {{Schema}}.bug4953_sleep_insert();
                      """);

        try
        {
            using var daemon = await StartDaemon();

            // the very first event of the store is slow: its transaction holds seq 1 in flight ~4s
            var slowAppend = Task.Run(async () =>
            {
                await using var session = theStore.LightweightSession();
                session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(viewId, 0));
                await session.SaveChangesAsync();
            });

            await Task.Delay(300);

            // meanwhile 60 later events commit normally
            for (var i = 0; i < 20; i++)
            {
                await using var session = theStore.LightweightSession();
                session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(viewId, i + 1));
                session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(viewId, 100 + i));
                session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(viewId, 200 + i));
                await session.SaveChangesAsync();
            }

            await slowAppend;

            await daemon.WaitForNonStaleData(60.Seconds());

            var persisted = await scalar($"select count(*) from {Schema}.mt_events");
            await using var query = theStore.QuerySession();
            var views = await query.Query<Bug4953GapView>().ToListAsync();
            var projected = views.SelectMany(x => x.Sequences).Count();

            _output.WriteLine($"persisted={persisted}, projected={projected}");
            projected.ShouldBe((int)persisted);
        }
        finally
        {
            await execute($"""
                          DROP TRIGGER IF EXISTS bug4953_sleep_insert ON {Schema}.mt_events;
                          DROP FUNCTION IF EXISTS {Schema}.bug4953_sleep_insert();
                          """);
        }
    }

}

public sealed record Bug4953GapEvent(Guid AggregateId, int Number);

public class Bug4953GapView
{
    public Guid Id { get; set; }
    public List<long> Sequences { get; set; } = new();
}

public partial class Bug4953GapViewProjection: MultiStreamProjection<Bug4953GapView, Guid>
{
    public Bug4953GapViewProjection()
    {
        Identity<Bug4953GapEvent>(x => x.AggregateId);
    }

    public void Apply(Bug4953GapView view, IEvent<Bug4953GapEvent> e)
    {
        view.Sequences.Add(e.Sequence);
    }
}
