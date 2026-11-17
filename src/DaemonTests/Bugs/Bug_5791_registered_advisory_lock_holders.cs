using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
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
/// #5791: Wolverine's transaction-scoped durability pass lock is a connection that sits "idle in
/// transaction" holding pg_advisory_xact_lock for the whole pass and never appends. Gap detection
/// counted it as a possible reserver of any gap first seen during the pass, so the daemon held behind
/// it. IgnoreAdvisoryLockHoldersForGapDetection() lets an integration register that lock key.
/// </summary>
public class Bug_5791_registered_advisory_lock_holders: DaemonContext
{
    // The id from the issue's repro: Wolverine's lock ids are int hashes and often negative, which as a
    // bigint advisory key means a non-zero classid in pg_locks
    private const long WolverineStyleLockKey = -568490066;

    public Bug_5791_registered_advisory_lock_holders(ITestOutputHelper output): base(output)
    {
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    private async Task<NpgsqlConnection> openConnection()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        return conn;
    }

    // The shape of Wolverine's pass lock: a dedicated connection, a transaction, a set_config() and the lock
    // call at acquisition, and then nothing but "idle in transaction" until the pass ends
    private async Task<NpgsqlConnection> startPassLockSession(long lockKey)
    {
        var conn = await openConnection();
        await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await conn.CreateCommand(
                $"select set_config('application_name', 'durability-pass', true); select pg_try_advisory_xact_lock({lockKey})")
            .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return conn;
    }

    private async Task appendEvent(Guid streamId)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, new Bug5791Counted());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // One event, two sequence numbers nobody ever uses, another event: a dead gap at 2..3 that exists
    // before any daemon polls, so no allocation fence can rule anything out
    private async Task<Guid> createDeadGap()
    {
        var streamId = Guid.NewGuid();
        await appendEvent(streamId);

        await using (var conn = await openConnection())
        {
            await conn.CreateCommand(
                    $"select nextval('{Schema}.mt_events_sequence'); select nextval('{Schema}.mt_events_sequence')")
                .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await appendEvent(streamId);
        return streamId;
    }

    private void configure(bool register)
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
            opts.Projections.Snapshot<Bug5791Counter>(SnapshotLifecycle.Async);

            if (register)
            {
                opts.Projections.IgnoreAdvisoryLockHoldersForGapDetection(WolverineStyleLockKey);
            }
        });

        theStore.EnsureStorageExists(typeof(IEvent));
    }

    [Fact]
    public async Task an_unregistered_pass_lock_session_holds_the_daemon_behind_a_dead_gap()
    {
        configure(register: false);
        await createDeadGap();

        await using var passLock = await startPassLockSession(WolverineStyleLockKey);

        var daemon = await StartDaemon();
        await Should.ThrowAsync<TimeoutException>(() => daemon.WaitForNonStaleData(5.Seconds()));
    }

    [Fact]
    public async Task a_registered_pass_lock_session_does_not_hold_the_daemon_behind_a_dead_gap()
    {
        configure(register: true);
        var streamId = await createDeadGap();

        await using var passLock = await startPassLockSession(WolverineStyleLockKey);

        var daemon = await StartDaemon();
        await daemon.WaitForNonStaleData(15.Seconds());

        await using var query = theStore.QuerySession();
        var counter = await query.LoadAsync<Bug5791Counter>(streamId, TestContext.Current.CancellationToken);
        counter.ShouldNotBeNull().Count.ShouldBe(2);
    }

    /// <summary>
    /// Registering a key never rules out a session that has written something: a session with an xid
    /// could be the gap's reserver, whatever lock it also holds.
    /// </summary>
    [Fact]
    public async Task a_registered_lock_holder_that_has_written_still_holds_the_gap()
    {
        configure(register: true);

        await appendEvent(Guid.NewGuid());

        // The reserver takes the registered lock, then reserves a sequence number and writes
        await using var reserver = await openConnection();
        var tx = await reserver.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await reserver.CreateCommand($"select pg_advisory_xact_lock({WolverineStyleLockKey})")
            .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var seq = (long)(await reserver.CreateCommand($"select nextval('{Schema}.mt_events_sequence')")
            .ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        seq.ShouldBe(2);
        await reserver.CreateCommand($@"
insert into {Schema}.mt_events(seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type, is_archived)
select {seq}, gen_random_uuid(), stream_id, 100000 + {seq}, data, type, now(), tenant_id, mt_dotnet_type, false
from {Schema}.mt_events where seq_id = 1").ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await appendEvent(Guid.NewGuid()); // 3, committed above the outstanding 2

        var detector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            NullLogger.Instance);

        (await detector.DetectInSafeZone(CancellationToken.None)).CurrentMark.ShouldBe(1);
        await Task.Delay(700, TestContext.Current.CancellationToken);

        var held = await detector.DetectInSafeZone(CancellationToken.None);
        held.CurrentMark.ShouldBe(1);
        held.IncludesSkipping.ShouldBeFalse();

        await tx.RollbackAsync(TestContext.Current.CancellationToken);
    }
}

public record Bug5791Counted;

public class Bug5791Counter
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(Bug5791Counted e) => Count++;
}
