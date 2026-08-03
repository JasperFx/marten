using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.ManualOnly.Coordination;

/// <summary>
/// End-to-end proof for #5108 and #5125: a real HotCold daemon, real advisory locks, and deliberately
/// slow (permanently idle-in-transaction) sessions, against a store carrying a genuinely dead sequence
/// gap. These run against real wall-clock daemon polling rather than driving the detector directly,
/// which is the whole point — the reports are about what the running daemon does, and both of them
/// end with "the mark never advances again".
///
/// The gap-liveness probe (#4953/#4977) refuses to skip a gap while any transaction that could still
/// fill it looks alive, and its second clause counts ANY client backend whose transaction started
/// before the gap was observed. A session parked idle-in-transaction on an advisory lock — Wolverine's
/// exclusive listeners, or the daemon's OWN transaction-scoped leadership lock — trips that clause
/// forever, because it never commits. #5057's allocation fence rules such sessions out by proof, but
/// only while the detector instance that watched the sequence cross the mark is still alive.
///
/// Slow by construction (multi-second daemon polling and settle windows), so these live in
/// ManualOnly rather than the CI daemon suite.
/// </summary>
public class high_water_advances_past_dead_gaps_with_slow_transactions: DaemonContext
{
    public high_water_advances_past_dead_gaps_with_slow_transactions(ITestOutputHelper output): base(output)
    {
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    private readonly List<NpgsqlConnection> _slowSessions = new();

    #region helpers

    private async Task<NpgsqlConnection> openConnection()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// A purposefully slow transaction: grabs a transaction-scoped advisory lock and then sits
    /// idle-in-transaction indefinitely without running another statement. This is exactly the shape
    /// of Wolverine's exclusive listener sessions — the fleet in #5108 had 57 of them — and of the
    /// daemon's own leadership lock in #5125.
    /// </summary>
    private async Task startSlowAdvisoryLockSession(long advisoryKey)
    {
        var conn = await openConnection();
        await conn.BeginTransactionAsync();
        await conn.CreateCommand($"select pg_advisory_xact_lock({advisoryKey})")
            .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        _slowSessions.Add(conn);
    }

    private async Task closeSlowSessions()
    {
        foreach (var conn in _slowSessions)
        {
            await conn.DisposeAsync();
        }

        _slowSessions.Clear();
    }

    /// <summary>
    /// Burn a sequence number and roll it back — a rolled-back SaveChanges, which is how the reporters'
    /// optimistic-concurrency losers produce their gaps — then commit more events above the hole so the
    /// mark is genuinely pinned under a permanently dead sequence number.
    /// </summary>
    private async Task<long> createDeadGapAsync()
    {
        long seq;
        await using (var conn = await openConnection())
        {
            var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
            seq = (long)(await conn.CreateCommand($"select nextval('{Schema}.mt_events_sequence')")
                .ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

            // An in-flight append that holds the row but never commits
            await conn.CreateCommand($@"
insert into {Schema}.mt_events(seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type, is_archived)
select {seq}, gen_random_uuid(), stream_id, 100000 + {seq}, data, type, now(), tenant_id, mt_dotnet_type, false
from {Schema}.mt_events where seq_id = 1").ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        return seq;
    }

    /// <summary>
    /// Commit event rows straight through SQL rather than through <c>theStore</c>. The restart test
    /// has to keep writing after the first host is disposed, and disposing a host tears down the
    /// pooled data source that the context's own store is still holding (see #4874) — going direct
    /// keeps this test measuring high-water behavior instead of that disposal ordering.
    /// </summary>
    private async Task commitEventsDirectlyAsync(int count)
    {
        await using var conn = await openConnection();
        for (var i = 0; i < count; i++)
        {
            await conn.CreateCommand($@"
insert into {Schema}.mt_events(seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type, is_archived)
select nextval('{Schema}.mt_events_sequence'), gen_random_uuid(), stream_id, 200000 + {i}, data, type, now(), tenant_id, mt_dotnet_type, false
from {Schema}.mt_events where seq_id = 1").ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task<long> currentHighWaterMarkAsync()
    {
        await using var conn = await openConnection();
        var raw = await conn
            .CreateCommand(
                $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'")
            .ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return raw is long l ? l : Convert.ToInt64(raw ?? 0L);
    }

    private async Task<long> highestCommittedSequenceAsync()
    {
        await using var conn = await openConnection();
        var raw = await conn.CreateCommand($"select coalesce(max(seq_id), 0) from {Schema}.mt_events")
            .ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return raw is long l ? l : Convert.ToInt64(raw ?? 0L);
    }

    #endregion

    /// <summary>
    /// #5108 §1 — the deploy / crash / shard-rebalance case. A daemon runs and catches up (which is
    /// when the allocation fence is established), the process is replaced, and a dead gap is sitting
    /// there when the replacement starts. On master the replacement detector has no in-memory
    /// allocation history, the slow advisory-lock sessions read as live reservers, and the mark never
    /// advances again.
    /// </summary>
    [Fact]
    public async Task restarted_daemon_advances_past_a_dead_gap_with_slow_sessions_present()
    {
        // The Wolverine-listener fleet: open BEFORE anything else, so every one of them has an
        // xact_start older than any gap the detector will ever observe
        await startSlowAdvisoryLockSession(51080001);
        await startSlowAdvisoryLockSession(51080002);
        await startSlowAdvisoryLockSession(51080003);

        try
        {
            NumberOfStreams = 5;
            await PublishSingleThreaded();
            var published = NumberOfEvents;

            // First daemon generation: runs while the store is healthy and caught up. This is the only
            // window in which anything can observe the sequence's allocated high still at or below the
            // mark — i.e. the only chance to establish the fence.
            var first = await StartDaemonInHotColdMode();
            await first.Daemon().Tracker.WaitForHighWaterMark(published, 30.Seconds());
            _output.WriteLine($"First daemon generation caught up at {published}");

            // The deploy: the process holding that in-memory history goes away
            await first.StopAsync(TestContext.Current.CancellationToken);
            first.Dispose();

            // ...and a dead gap forms while no daemon is running
            var deadSeq = await createDeadGapAsync();
            _output.WriteLine($"Dead sequence number: {deadSeq}");

            await commitEventsDirectlyAsync(3);
            var ceiling = await highestCommittedSequenceAsync();
            _output.WriteLine($"Highest committed sequence above the gap: {ceiling}");

            // The replacement node starts up against a store that was already gapped
            using var second = await StartDaemonInHotColdMode();
            await second.Daemon().Tracker.WaitForHighWaterMark(ceiling, 60.Seconds());

            (await currentHighWaterMarkAsync()).ShouldBe(ceiling);
        }
        finally
        {
            await closeSlowSessions();
        }
    }

    /// <summary>
    /// #5125 — no restart and no third-party session needed. The store is already gapped before any
    /// daemon has ever run (events appended then deleted; a rolled-back append leaves the same state),
    /// so no fence can be established by construction. The only open transaction is the daemon's OWN
    /// HotCold leadership lock, which is transaction-scoped by default and therefore sits
    /// idle-in-transaction for its entire tenure — and gap detection counts it as a possible reserver
    /// of the very gap it is trying to skip.
    /// </summary>
    [Fact]
    public async Task daemon_advances_when_its_own_advisory_lock_is_the_only_open_transaction()
    {
        // Poison the sequence BEFORE any daemon exists: append, then delete the rows, leaving the
        // sequence's allocated values above a high water mark that is still 0
        NumberOfStreams = 3;
        await PublishSingleThreaded();

        await using (var conn = await openConnection())
        {
            await conn.CreateCommand($"delete from {Schema}.mt_events")
                .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await conn.CreateCommand($"delete from {Schema}.mt_streams")
                .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await conn.CreateCommand($"delete from {Schema}.mt_event_progression")
                .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        (await currentHighWaterMarkAsync()).ShouldBe(0);

        // Now real events land above the permanent hole
        NumberOfStreams = 4;
        await PublishSingleThreaded();
        var ceiling = await highestCommittedSequenceAsync();
        _output.WriteLine($"Highest committed sequence above the poisoned range: {ceiling}");

        using var host = await StartDaemonInHotColdMode();
        await host.Daemon().Tracker.WaitForHighWaterMark(ceiling, 60.Seconds());

        (await currentHighWaterMarkAsync()).ShouldBe(ceiling);
    }
}
