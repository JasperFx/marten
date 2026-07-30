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

namespace DaemonTests.Bugs;

/// <summary>
/// #5091, follow-up to #4953/#5057. The allocation fence used to be built from the sequence's
/// RESERVED ceiling (<c>last_value</c>), and Postgres reports <c>last_value = 1</c> for a sequence
/// nothing has drawn from yet. No poll could ever report a value at or below a stuck mark of 0, so a
/// leading gap — the mark pinned at 0 under a hole at the very start of the sequence — could never be
/// fenced, and any permanently idle open transaction held it forever. That is the shape in #5090:
/// "Daemon high water detection is holding before the sequence gap above 0". Reading <c>is_called</c>
/// as well gives the highest ALLOCATED value, which is 0 for a pristine sequence, and mark 0 becomes
/// fenceable without weakening the proof.
/// </summary>
public class Bug_5091_allocation_fence_at_mark_zero: DaemonContext
{
    private readonly ITestOutputHelper _output;

    public Bug_5091_allocation_fence_at_mark_zero(ITestOutputHelper output): base(output)
    {
        _output = output;
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    private async Task<NpgsqlConnection> openConnection()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // The #5090 zombie: a session that took a transaction-scoped advisory lock and will never run
    // another statement. Its xact_start predates every gap, so the unfenced liveness probe counts it
    // as a possible reserver forever.
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
            session.Events.StartStream(Guid.NewGuid(), new Bug5091GapEvent(Guid.NewGuid(), i + 1));
        }

        await session.SaveChangesAsync();
    }

    private async Task<long> scalar(string sql)
    {
        await using var conn = await openConnection();
        var raw = await conn.CreateCommand(sql).ExecuteScalarAsync();
        return raw is long l ? l : Convert.ToInt64(raw ?? 0L);
    }

    private HighWaterDetector buildDetector()
    {
        return new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            NullLogger.Instance);
    }

    [Fact]
    public async Task a_dead_leading_gap_skips_despite_an_idle_advisory_lock_session()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        // The zombie is already parked before anything is appended — exactly the #5090 ordering,
        // where the leaked session came from an earlier host in the same process.
        var listener = await startIdleAdvisoryLockSession(5091001);
        try
        {
            var detector = buildDetector();

            // The fence-enabling reading: a poll over a pristine sequence. last_value is 1 here, but
            // is_called is false, so nothing has been handed out and the allocated high is 0.
            var baseline = await detector.Detect(CancellationToken.None);
            baseline.CurrentMark.ShouldBe(0);

            // seq 1 is reserved and rolled back: a permanently dead hole at the very start.
            await using (var conn = await openConnection())
            {
                var tx = await conn.BeginTransactionAsync();
                var seq = (long)(await conn.CreateCommand($"select nextval('{Schema}.mt_events_sequence')")
                    .ExecuteScalarAsync())!;
                seq.ShouldBe(1);
                await tx.RollbackAsync(TestContext.Current.CancellationToken);
            }

            await appendEvents(3); // 2..4 committed

            // First sighting — the stale threshold is measured from here, so this one holds.
            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(0);

            await Task.Delay(700, TestContext.Current.CancellationToken);

            // Past the threshold. The only open transaction older than the gap is the idle advisory
            // lock session, and it has provably executed nothing since before seq 1 was allocated, so
            // it cannot be the reserver. The gap is dead: skip.
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"Leading gap past threshold with idle listener: CurrentMark={second.CurrentMark}");
            second.CurrentMark.ShouldBe(4);
            second.IncludesSkipping.ShouldBeTrue();

            var persisted = await scalar(
                $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
            persisted.ShouldBe(4);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task a_live_reserver_of_the_leading_gap_still_holds_the_mark()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(5091002);
        try
        {
            var detector = buildDetector();
            (await detector.Detect(CancellationToken.None)).CurrentMark.ShouldBe(0);

            // seq 1 reserved by a transaction that is still alive. It called nextval AFTER the fence,
            // which bumped its state_change, so the fence must keep it even though the idle listener
            // is ruled out. Fencing mark 0 must not become a licence to skip live appends.
            var conn = await openConnection();
            var tx = await conn.BeginTransactionAsync();
            try
            {
                var seq = (long)(await conn.CreateCommand($"select nextval('{Schema}.mt_events_sequence')")
                    .ExecuteScalarAsync())!;
                seq.ShouldBe(1);

                await appendEvents(3); // 2..4 committed

                (await detector.DetectInSafeZone(CancellationToken.None)).CurrentMark.ShouldBe(0);

                await Task.Delay(700, TestContext.Current.CancellationToken);
                var held = await detector.DetectInSafeZone(CancellationToken.None);
                _output.WriteLine($"Leading gap past threshold with LIVE reserver: CurrentMark={held.CurrentMark}");
                held.CurrentMark.ShouldBe(0);

                await tx.RollbackAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                await conn.DisposeAsync();
            }

            // The reserver died, so now the gap is provably dead.
            await Task.Delay(200, TestContext.Current.CancellationToken);
            var after = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"After reserver death: CurrentMark={after.CurrentMark}");
            after.CurrentMark.ShouldBe(4);
            after.IncludesSkipping.ShouldBeTrue();
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task a_detector_that_never_saw_the_pristine_sequence_still_holds_conservatively()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(5091003);
        try
        {
            // The gap forms before this detector ever polls — the fresh-host-over-an-existing-database
            // case. There is no proof of when seq 1 was allocated, so the idle session stays a
            // candidate reserver and the documented conservative hold remains (#5090's actual shape;
            // its real fix is the Weasel handle strand, not this fence).
            await using (var conn = await openConnection())
            {
                var tx = await conn.BeginTransactionAsync();
                await conn.CreateCommand($"select nextval('{Schema}.mt_events_sequence')").ExecuteScalarAsync();
                await tx.RollbackAsync(TestContext.Current.CancellationToken);
            }

            await appendEvents(3); // 2..4 committed

            var detector = buildDetector();
            (await detector.DetectInSafeZone(CancellationToken.None)).CurrentMark.ShouldBe(0);

            await Task.Delay(700, TestContext.Current.CancellationToken);
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"Fenceless leading gap past threshold: CurrentMark={second.CurrentMark}");
            second.CurrentMark.ShouldBe(0);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }
}

public record Bug5091GapEvent(Guid Id, int Number);
