using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events.Daemon.HighWater;
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
/// Follow-up to #4953: the gap-liveness gate must not be held hostage by open transactions that are
/// provably NOT the gap's reserver. Wolverine's exclusive listeners park an advisory lock inside an
/// idle-in-transaction session for the life of the process — to the unrefined probe every one of
/// them is forever a "possible living reserver" (its xact_start predates any gap), so a genuinely
/// dead gap never skips and projections freeze until a human intervenes. The allocation fence rules
/// them out by proof: the detector remembers when the reserved last_value was still at or below the
/// stuck mark, so any session that has executed nothing since then cannot have called nextval for a
/// gap sequence.
/// </summary>
public class Bug_4953_idle_advisory_lock_sessions_do_not_block_gap_skips: DaemonContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4953_idle_advisory_lock_sessions_do_not_block_gap_skips(ITestOutputHelper output): base(output)
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

    // The Wolverine-style exclusive listener: an idle-in-transaction session that grabbed a
    // transaction-scoped advisory lock long ago and will never run another statement.
    private async Task<NpgsqlConnection> startIdleAdvisoryLockSession(long lockId)
    {
        var conn = await openConnection();
        await conn.BeginTransactionAsync();
        await conn.CreateCommand($"select pg_advisory_xact_lock({lockId})").ExecuteNonQueryAsync();
        return conn;
    }

    private async Task appendEvents(int count)
    {
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < count; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new Bug4953GapEvent(Guid.NewGuid(), i + 1));
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

    // Reserves the next sequence number and inserts its event row WITHOUT committing, exactly like
    // an in-flight SaveChanges
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

    #endregion

    [Fact]
    public async Task dead_gap_skips_despite_an_ancient_idle_in_transaction_advisory_lock_session()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(4953001);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            var detector = buildDetector();

            // A normal poll while the store is caught up — this is what gives the detector proof
            // that no sequence number above 8 existed yet at this moment
            var baseline = await detector.Detect(CancellationToken.None);
            baseline.CurrentMark.ShouldBe(8);

            // seq 9 is reserved and rolled back: a permanently dead hole
            var (conn, tx, seq) = await startOutstandingAppend();
            try
            {
                seq.ShouldBe(9);
                await appendEvents(3); // 10..12 committed
                await tx.RollbackAsync();
            }
            finally
            {
                await conn.DisposeAsync();
            }

            // First sighting of the gap — hold (threshold measured from first observation)
            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8);

            await Task.Delay(700);

            // Threshold elapsed, and the ONLY open transaction from before the gap is the advisory
            // lock listener, which has provably executed nothing since before seq 9 was allocated.
            // The gap is dead: skip — with NO SkipStaleGapsDespiteLiveTransactionsAfter configured.
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"After threshold with idle listener present: CurrentMark={second.CurrentMark}");
            second.CurrentMark.ShouldBe(12);
            second.IncludesSkipping.ShouldBeTrue();

            var persisted = await scalar(
                $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
            persisted.ShouldBe(12);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task a_genuinely_live_reserver_still_holds_the_gap_when_the_fence_is_present()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(4953002);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            var detector = buildDetector();
            (await detector.Detect(CancellationToken.None)).CurrentMark.ShouldBe(8);

            // seq 9 reserved by a transaction that is STILL alive — it ran its statements after the
            // fence, so the fence must not rule it out even while the idle listener IS ruled out
            var (conn, tx, seq) = await startOutstandingAppend();
            try
            {
                seq.ShouldBe(9);
                await appendEvents(3); // 10..12 committed

                var first = await detector.DetectInSafeZone(CancellationToken.None);
                first.CurrentMark.ShouldBe(8);

                await Task.Delay(700);
                var second = await detector.DetectInSafeZone(CancellationToken.None);
                _output.WriteLine($"Past threshold with live reserver: CurrentMark={second.CurrentMark}");
                second.CurrentMark.ShouldBe(8);

                // The reserver dies — NOW the gap is provably dead and the skip proceeds
                await tx.RollbackAsync();
            }
            finally
            {
                await conn.DisposeAsync();
            }

            await Task.Delay(200);
            var third = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"After reserver death: CurrentMark={third.CurrentMark}");
            third.CurrentMark.ShouldBe(12);
            third.IncludesSkipping.ShouldBeTrue();
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task without_allocation_history_the_idle_session_conservatively_holds_the_gap()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(4953003);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            // The dead gap forms BEFORE the detector ever polls — a daemon restarted mid-gap has no
            // proof of when the gap's sequence numbers were allocated, so the idle open transaction
            // stays a candidate reserver and the hold remains (the documented conservative
            // degradation; SkipStaleGapsDespiteLiveTransactionsAfter is the backstop here)
            var (conn, tx, seq) = await startOutstandingAppend();
            try
            {
                seq.ShouldBe(9);
                await appendEvents(3); // 10..12 committed
                await tx.RollbackAsync();
            }
            finally
            {
                await conn.DisposeAsync();
            }

            var detector = buildDetector();
            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8);

            await Task.Delay(700);
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"Past threshold, fenceless: CurrentMark={second.CurrentMark}");
            second.CurrentMark.ShouldBe(8);

            var persisted = await scalar(
                $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
            persisted.ShouldBe(8);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }
}
