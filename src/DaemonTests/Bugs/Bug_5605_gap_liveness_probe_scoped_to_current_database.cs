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
/// The gap-liveness gate must only weigh transactions that could actually be THIS event store's
/// reserver, which means transactions in this database. Two of the probe's three clauses already say
/// so (pg_locks carries a database oid, pg_stat_activity carries datname); the third does not.
/// <c>pg_snapshot_xip(pg_current_snapshot())</c> is cluster-wide — a snapshot's in-progress xid list
/// spans every database on the server, because xids are allocated from one cluster-wide counter — so
/// a write transaction in an unrelated database counts as a possible reserver of a gap in ours.
///
/// <para>
/// Reported by a client running a large test suite in parallel, one database per test, all on one
/// server. Under load some neighbouring database always has a write transaction older than the gap,
/// so <c>older_write_xids</c> never falls to zero, every dead gap looks outstanding forever, and the
/// high water mark stops advancing. Nothing about the holding store is wrong, and nothing in the
/// store's own database explains the hold — which is what makes it so hard to read from the log.
/// </para>
///
/// <para>
/// The clause earns its place and cannot simply be dropped: it is the only signal that sees a writer
/// belonging to another ROLE, because pg_stat_activity redacts xact_start/state for sessions an
/// unprivileged viewer does not own. It needs scoping, not removal — and the two columns the scoping
/// needs (pg_locks in full, pg_stat_activity.datname) are both readable cross-role, so scoping costs
/// none of that coverage.
/// </para>
/// </summary>
public class Bug_5605_gap_liveness_probe_scoped_to_current_database: DaemonContext
{
    public Bug_5605_gap_liveness_probe_scoped_to_current_database(ITestOutputHelper output): base(output)
    {
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    #region helpers

    private async Task<NpgsqlConnection> openConnection()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// A write transaction in a DIFFERENT database on the SAME server — the neighbouring test
    /// database of the report, reduced to its essentials. pg_current_xact_id() forces a real xid
    /// without needing any schema there, so the maintenance database every server already has will
    /// do and the test creates nothing it has to drop.
    /// </summary>
    private async Task<NpgsqlConnection> startWriteTransactionInAnotherDatabase()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            Database = "postgres"
        };

        var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await conn.BeginTransactionAsync();
        await conn.CreateCommand("select pg_current_xact_id()").ExecuteNonQueryAsync();
        return conn;
    }

    /// <summary>
    /// Keeps the neighbour out of the probe's "quiescent" exclusion, which rules out sessions that
    /// have provably executed nothing since before the gap's sequence numbers were allocated. A
    /// database busy running tests is executing statements constantly, so its state_change is always
    /// fresh — a neighbour parked permanently idle would be excluded and would not reproduce this.
    /// </summary>
    private static Task touch(NpgsqlConnection conn)
    {
        return conn.CreateCommand("select 1").ExecuteNonQueryAsync();
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
    public async Task dead_gap_skips_despite_a_live_write_transaction_in_a_neighbouring_database()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        await using var neighbour = await startWriteTransactionInAnotherDatabase();

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        var detector = buildDetector();

        // A normal poll while the store is caught up. This is what establishes the allocation fence:
        // proof that no sequence number above 8 existed yet at this moment.
        var baseline = await detector.Detect(CancellationToken.None);
        baseline.CurrentMark.ShouldBe(8);

        // The neighbour is mid-test, not parked: it has run a statement since the fence, so the
        // quiescent exclusion does not apply to it and only the xid clause can be counting it.
        await touch(neighbour);

        // seq 9 is reserved and rolled back: a permanently dead hole in THIS database
        var (conn, tx, seq) = await startOutstandingAppend();
        try
        {
            seq.ShouldBe(9);
            await appendEvents(3); // 10..12 committed, which also advances the cluster xid counter
                                   // past the neighbour's xid so it falls below the recorded xmax
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await conn.DisposeAsync();
        }

        // First sighting of the gap — hold, because the stale threshold is measured from here
        var first = await detector.DetectInSafeZone(CancellationToken.None);
        first.CurrentMark.ShouldBe(8);

        await Task.Delay(700, TestContext.Current.CancellationToken);
        await touch(neighbour);

        // The threshold has elapsed and this database holds no transaction that could fill seq 9:
        // the reserver rolled back. The only write transaction older than the gap anywhere on the
        // server belongs to another database and cannot touch this event store's sequence, so the
        // gap is proven dead and the mark must move to 12.
        var second = await detector.DetectInSafeZone(CancellationToken.None);
        second.CurrentMark.ShouldBe(12);
        second.IncludesSkipping.ShouldBeTrue();

        var persisted = await scalar(
            $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
        persisted.ShouldBe(12);
    }

    [Fact]
    public async Task a_live_reserver_in_this_database_still_holds_the_gap()
    {
        // The scoping must not cost the probe its actual job. Same shape as above, except the
        // transaction holding seq 9 open lives in THIS database and is still running.
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        await using var neighbour = await startWriteTransactionInAnotherDatabase();

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        var detector = buildDetector();
        (await detector.Detect(CancellationToken.None)).CurrentMark.ShouldBe(8);
        await touch(neighbour);

        var (conn, tx, seq) = await startOutstandingAppend();
        try
        {
            seq.ShouldBe(9);
            await appendEvents(3); // 10..12 committed

            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8);

            await Task.Delay(700, TestContext.Current.CancellationToken);

            // Still held: the reserver in this database is alive and its event may yet commit
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            second.CurrentMark.ShouldBe(8);

            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await conn.DisposeAsync();
        }

        await Task.Delay(200, TestContext.Current.CancellationToken);
        var third = await detector.DetectInSafeZone(CancellationToken.None);
        third.CurrentMark.ShouldBe(12);
        third.IncludesSkipping.ShouldBeTrue();
    }
}
