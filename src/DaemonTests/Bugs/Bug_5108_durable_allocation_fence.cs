using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
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
/// #5108 / #5125: the allocation fence that rules permanently-idle open transactions out as gap
/// reservers (#5057) used to live only in the detector instance's memory, so a daemon that started or
/// resumed AFTER a gap formed had none and the mark never advanced again. These pin the two things
/// that make the default configuration recover: the fence is durable across detector generations, and
/// the daemon's own advisory-lock connections are ruled out structurally.
///
/// The end-to-end versions — real HotCold daemons and real leadership locks — live in
/// DaemonTests.ManualOnly.Coordination.high_water_advances_past_dead_gaps_with_slow_transactions.
/// </summary>
public class Bug_5108_durable_allocation_fence: DaemonContext
{
    private readonly ITestOutputHelper _output;

    public Bug_5108_durable_allocation_fence(ITestOutputHelper output): base(output)
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

    private async Task<NpgsqlConnection> startIdleAdvisoryLockSession(long lockId)
    {
        var conn = await openConnection();
        await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await conn.CreateCommand($"select pg_advisory_xact_lock({lockId})")
            .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return conn;
    }

    private async Task appendEvents(int count)
    {
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < count; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new Bug5108FenceEvent(i + 1));
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task execute(string sql)
    {
        await using var conn = await openConnection();
        await conn.CreateCommand(sql).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<long> scalar(string sql)
    {
        await using var conn = await openConnection();
        var raw = await conn.CreateCommand(sql).ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return raw is long l ? l : Convert.ToInt64(raw ?? 0L);
    }

    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx, long seq)> startOutstandingAppend()
    {
        var conn = await openConnection();
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var seq = (long)(await conn.CreateCommand($"select nextval('{Schema}.mt_events_sequence')")
            .ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        await conn.CreateCommand($@"
insert into {Schema}.mt_events(seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type, is_archived)
select {seq}, gen_random_uuid(), stream_id, 100000 + {seq}, data, type, now(), tenant_id, mt_dotnet_type, false
from {Schema}.mt_events where seq_id = 1").ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return (conn, tx, seq);
    }

    private HighWaterDetector buildDetector()
    {
        return new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            NullLogger.Instance);
    }

    #endregion

    /// <summary>
    /// The #5108 §1 shape: a daemon runs and catches up (establishing the fence), the process is
    /// replaced, and a dead gap is already sitting there when the replacement starts. The replacement
    /// detector has no in-memory history whatsoever and must still be able to prove the gap dead.
    /// </summary>
    [Fact]
    public async Task a_fresh_detector_inherits_the_fence_its_predecessor_persisted()
    {
        StoreOptions(opts => opts.Projections.StaleSequenceThreshold = 500.Milliseconds());
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(5108101);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            // Generation 1: polls while caught up, which is the only moment the sequence's allocated
            // high is observably at or below the mark
            var first = buildDetector();
            (await first.Detect(CancellationToken.None)).CurrentMark.ShouldBe(8);

            (await scalar(
                    $"select coalesce(max(last_seq_id), -1) from {Schema}.mt_event_progression where name = '{HighWaterAllocationFence.ProgressionName}'"))
                .ShouldBe(8);

            // The gap forms after that generation is gone
            var (conn, tx, seq) = await startOutstandingAppend();
            try
            {
                seq.ShouldBe(9);
                await appendEvents(3); // 10..12 committed
                await tx.RollbackAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                await conn.DisposeAsync();
            }

            // Generation 2 — a brand new detector, exactly what a redeployed process gets
            var second = buildDetector();
            (await second.DetectInSafeZone(CancellationToken.None)).CurrentMark.ShouldBe(8);

            await Task.Delay(700, TestContext.Current.CancellationToken);

            var skipped = await second.DetectInSafeZone(CancellationToken.None);
            skipped.CurrentMark.ShouldBe(12);
            skipped.IncludesSkipping.ShouldBeTrue();
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    /// <summary>
    /// The safety counterpart: a persisted fence must not turn into a licence to skip past a
    /// transaction that really is still holding the gap. The reserver ran its statements after the
    /// fence was recorded, so it is never quiescent and the mark has to hold.
    /// </summary>
    [Fact]
    public async Task a_persisted_fence_does_not_skip_past_a_genuinely_live_reserver()
    {
        StoreOptions(opts => opts.Projections.StaleSequenceThreshold = 500.Milliseconds());
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(5108102);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            var first = buildDetector();
            (await first.Detect(CancellationToken.None)).CurrentMark.ShouldBe(8);

            var (conn, tx, seq) = await startOutstandingAppend();
            try
            {
                seq.ShouldBe(9);
                await appendEvents(3);

                // A fresh detector with only the PERSISTED fence to go on, against a reserver that is
                // still alive — it must hold, no matter how long it waits
                var second = buildDetector();
                (await second.DetectInSafeZone(CancellationToken.None)).CurrentMark.ShouldBe(8);

                await Task.Delay(700, TestContext.Current.CancellationToken);
                var stillHeld = await second.DetectInSafeZone(CancellationToken.None);
                stillHeld.CurrentMark.ShouldBe(8);

                await tx.RollbackAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                await conn.DisposeAsync();
            }
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    /// <summary>
    /// #5125: no fence can exist at all here — the store was gapped before any detector ever polled —
    /// and the only open transaction is one of the daemon's own leadership locks. That connection
    /// exists solely to hold the lock and never appends, so it must not be counted as a reserver.
    /// </summary>
    [Fact]
    public async Task the_daemons_own_advisory_lock_session_does_not_hold_a_dead_gap()
    {
        StoreOptions(opts => opts.Projections.StaleSequenceThreshold = 500.Milliseconds());
        theStore.EnsureStorageExists(typeof(IEvent));

        // DaemonContext gives every test class its own DaemonLockId, and the base id is always part of
        // the set the detector derives — this is the same lock a HotCold coordinator would be holding
        var daemonLock = await startIdleAdvisoryLockSession(theStore.Options.Projections.DaemonLockId);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            // The gap forms before ANY detector poll, so no fence — in memory or persisted — can exist
            var (conn, tx, seq) = await startOutstandingAppend();
            try
            {
                seq.ShouldBe(9);
                await appendEvents(3); // 10..12 committed
                await tx.RollbackAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                await conn.DisposeAsync();
            }

            var detector = buildDetector();
            (await detector.DetectInSafeZone(CancellationToken.None)).CurrentMark.ShouldBe(8);

            (await scalar(
                    $"select count(*) from {Schema}.mt_event_progression where name = '{HighWaterAllocationFence.ProgressionName}'"))
                .ShouldBe(0);

            await Task.Delay(700, TestContext.Current.CancellationToken);

            var skipped = await detector.DetectInSafeZone(CancellationToken.None);
            skipped.CurrentMark.ShouldBe(12);
            skipped.IncludesSkipping.ShouldBeTrue();
        }
        finally
        {
            await daemonLock.DisposeAsync();
        }
    }

    /// <summary>
    /// TryCorrectProgressInDatabaseAsync rewrites mt_event_progression with no WHERE clause. The fence
    /// row records an OBSERVED sequence allocation, not projection progress — restamping it would have
    /// it claim something that was never observed, and a fence dated later than the truth wrongly
    /// excludes a live reserver.
    /// </summary>
    [Fact]
    public async Task correcting_progress_leaves_the_fence_row_alone()
    {
        StoreOptions(opts => opts.Projections.StaleSequenceThreshold = 500.Milliseconds());
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        var detector = buildDetector();
        (await detector.Detect(CancellationToken.None)).CurrentMark.ShouldBe(8);

        var fenceBefore = await scalar(
            $"select last_seq_id from {Schema}.mt_event_progression where name = '{HighWaterAllocationFence.ProgressionName}'");
        fenceBefore.ShouldBe(8);

        // Drive the mark above the sequence height so the correction actually fires
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 999999)");
        await detector.TryCorrectProgressInDatabaseAsync(CancellationToken.None);

        (await scalar(
                $"select last_seq_id from {Schema}.mt_event_progression where name = 'HighWaterMark'"))
            .ShouldBe(8);

        (await scalar(
                $"select last_seq_id from {Schema}.mt_event_progression where name = '{HighWaterAllocationFence.ProgressionName}'"))
            .ShouldBe(fenceBefore);
    }

    /// <summary>
    /// The fence row is high-water bookkeeping, not a shard, and must never be reported as projection
    /// progress.
    /// </summary>
    [Fact]
    public async Task the_fence_row_is_not_reported_as_projection_progress()
    {
        StoreOptions(opts => opts.Projections.StaleSequenceThreshold = 500.Milliseconds());
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        var detector = buildDetector();
        await detector.Detect(CancellationToken.None);

        (await scalar(
                $"select count(*) from {Schema}.mt_event_progression where name = '{HighWaterAllocationFence.ProgressionName}'"))
            .ShouldBe(1);

        var progress =
            await theStore.Advanced.AllProjectionProgress(token: TestContext.Current.CancellationToken);
        progress.ShouldNotContain(x => x.ShardName == HighWaterAllocationFence.ProgressionName);
    }
}

public record Bug5108FenceEvent(int Number);
