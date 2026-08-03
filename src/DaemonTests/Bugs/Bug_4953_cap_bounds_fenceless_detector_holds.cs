using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// Follow-up to #4953/#5057: SkipStaleGapsDespiteLiveTransactionsAfter is documented as the bounded
/// override for the transaction-evidence hold — once a gap has been stuck past the cap, the skip
/// proceeds even though a candidate reserver still appears alive. A detector that first observes an
/// ALREADY-DEAD gap (daemon resumed mid-gap) has no allocation history, so the fence cannot rule
/// out a Wolverine-style idle-in-transaction listener and the evidence hold engages — exactly the
/// situation the cap exists to bound. The per-gap clock is detector-scoped, though, so a cap
/// measured from it alone restarts with every daemon resume and managed-distribution agent churn
/// can postpone the override forever. What keeps the cap a bound on the STALL rather than on one
/// detector's observation of it is durable gap evidence: the earliest committed event above the
/// pinned mark postdates the gap's birth, clamped no earlier than the mark's last advance.
/// mt_event_progression.last_updated alone is never trusted — it records the last ADVANCE and ages
/// on a caught-up idle store exactly like on a stuck one — and with nothing committed above the
/// mark only the running detector's own clock counts, so a live first append after an idle stretch
/// is held for, not capped away.
/// </summary>
public class Bug_4953_cap_bounds_fenceless_detector_holds: DaemonContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4953_cap_bounds_fenceless_detector_holds(ITestOutputHelper output): base(output)
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

    private async Task appendEvents(int count, string? tenantId = null)
    {
        await using var session = tenantId == null
            ? theStore.LightweightSession()
            : theStore.LightweightSession(tenantId);
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
            new TestLogger<HighWaterDetector>(_output));
    }

    #endregion

    [Fact]
    public async Task cap_skips_a_pre_existing_dead_gap_despite_an_idle_listener_and_no_allocation_history()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            opts.Projections.SkipStaleGapsDespiteLiveTransactionsAfter = 500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(4953004);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            // The dead gap forms BEFORE the detector ever polls — a daemon resumed mid-gap has no
            // allocation history, so the fence cannot exonerate the idle listener and the evidence
            // hold engages. The cap is configured, so the hold must be bounded.
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

            // First sighting of the pre-existing gap — hold (the per-gap clock starts here)
            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8);

            await Task.Delay(700, TestContext.Current.CancellationToken);

            // Threshold AND cap have both elapsed. The idle listener still reads as a possible live
            // reserver (no fence to exonerate it), but the cap must bound that hold and skip anyway.
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"Past cap, fenceless: CurrentMark={second.CurrentMark}");
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
    public async Task idle_store_first_live_append_is_not_cap_skipped()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            // A generous 10-minute cap: nothing in this test is stuck anywhere near that long
            opts.Projections.SkipStaleGapsDespiteLiveTransactionsAfter = 10.Minutes();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");
        // The idle stretch: the mark last advanced an hour ago because nothing was appended —
        // last_updated ages on a caught-up store exactly like on a stuck one, so it must never be
        // treated as stall evidence on its own
        await execute(
            $"update {Schema}.mt_event_progression set last_updated = transaction_timestamp() - interval '1 hour' where name = 'HighWaterMark'");

        // First append after the idle period: reserves seq 9, still in flight (a slow commit)
        var (conn, tx, seq) = await startOutstandingAppend();
        try
        {
            seq.ShouldBe(9);

            var detector = buildDetector();

            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8); // settle window

            await Task.Delay(400, TestContext.Current.CancellationToken);

            // The gap is ~400ms old, its reserver is provably alive, and nothing is committed above
            // the mark — there is NO evidence the gap predates this detector's observation, so the
            // 10-minute cap is nowhere near expired and the detector must HOLD
            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"CurrentMark after safe-zone pass: {second.CurrentMark}");
            second.CurrentMark.ShouldBe(8);

            // The append commits normally a moment later — its events are above the mark, projected
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    [Fact]
    public async Task live_reserver_is_not_cap_skipped_before_the_cap_genuinely_elapses()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            opts.Projections.SkipStaleGapsDespiteLiveTransactionsAfter = 2500.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(8);
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

        // seq 9 reserved by a transaction that stays alive throughout; 10..12 commit above it, so
        // durable gap evidence EXISTS — but it is young, and the cap (10x the threshold) has not
        // genuinely elapsed. Past the threshold the detector must still hold for the live reserver.
        var (conn, tx, seq) = await startOutstandingAppend();
        try
        {
            seq.ShouldBe(9);
            await appendEvents(3); // 10..12 committed

            var detector = buildDetector();

            var first = await detector.DetectInSafeZone(CancellationToken.None);
            first.CurrentMark.ShouldBe(8);

            await Task.Delay(400, TestContext.Current.CancellationToken);

            var second = await detector.DetectInSafeZone(CancellationToken.None);
            _output.WriteLine($"Past threshold, before cap: CurrentMark={second.CurrentMark}");
            second.CurrentMark.ShouldBe(8);

            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    [Fact]
    public async Task detector_churn_does_not_postpone_the_cap_forever()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            opts.Projections.SkipStaleGapsDespiteLiveTransactionsAfter = 600.Milliseconds();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(4953006);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

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

            // The stall is already older than the cap before any detector ever sees it
            await Task.Delay(700, TestContext.Current.CancellationToken);

            // Managed-distribution churn: each cycle is a freshly built daemon whose detector never
            // lives long enough to accumulate the cap on its own in-memory clock (300ms < 600ms).
            // The mark itself has been pinned since long before the cap, which mt_event_progression's
            // last_updated records durably — the cap must bound the STALL, not one detector's
            // observation of it, so some cycle here has to skip.
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var detector = buildDetector();

                var first = await detector.DetectInSafeZone(CancellationToken.None);
                first.CurrentMark.ShouldBe(8); // settle window for a just-appeared gap always holds

                await Task.Delay(300, TestContext.Current.CancellationToken);

                var second = await detector.DetectInSafeZone(CancellationToken.None);
                _output.WriteLine($"Cycle {cycle}: CurrentMark={second.CurrentMark}");
                if (second.CurrentMark > 8)
                {
                    second.CurrentMark.ShouldBe(12);
                    second.IncludesSkipping.ShouldBeTrue();

                    var persisted = await scalar(
                        $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
                    persisted.ShouldBe(12);
                    return;
                }
            }

            throw new ShouldAssertException(
                "The high water mark never skipped the dead gap: every detector restart reset the SkipStaleGapsDespiteLiveTransactionsAfter clock, so the cap never bounded the stall");
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task conjoined_tenancy_routes_store_global_and_the_cap_still_bounds_detector_churn()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            opts.Projections.SkipStaleGapsDespiteLiveTransactionsAfter = 600.Milliseconds();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(4953007);
        try
        {
            await appendEvents(8, "greenacres");
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            // A tenant append reserves from the SHARED sequence and rolls back — the burned number is
            // a hole in the one store-wide sequence, exactly the shape an optimistic-concurrency
            // loser leaves under conjoined tenancy
            var (conn, tx, seq) = await startOutstandingAppend();
            try
            {
                seq.ShouldBe(9);
                await appendEvents(3, "greenacres"); // 10..12 committed
                await tx.RollbackAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                await conn.DisposeAsync();
            }

            await Task.Delay(700, TestContext.Current.CancellationToken);

            // Conjoined tenancy shares one event sequence and one HighWaterMark row: the vectorized
            // per-tenant path is gated on UseTenantPartitionedEvents, NOT on TenancyStyle, so a
            // conjoined store's daemon detects through the same store-global path as a single-tenant
            // store — the per-tenant API collapses to the store-global reading
            var probe = buildDetector();
            probe.SupportsTenantPartitioning.ShouldBeFalse();
            var vector = await probe.DetectInSafeZoneForTenantsAsync(["greenacres"], CancellationToken.None);
            vector.TenantCount.ShouldBe(0);
            vector.Global.ShouldNotBeNull();
            vector.Global.CurrentMark.ShouldBe(8); // first sighting for this detector: settle hold

            // Same churn shape as detector_churn_does_not_postpone_the_cap_forever, on the conjoined
            // store: no single detector lives long enough to accumulate the cap in memory, so only
            // the durable pinned age can bound the stall
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var detector = buildDetector();

                var first = await detector.DetectInSafeZone(CancellationToken.None);
                first.CurrentMark.ShouldBe(8);

                await Task.Delay(300, TestContext.Current.CancellationToken);

                var second = await detector.DetectInSafeZone(CancellationToken.None);
                _output.WriteLine($"Cycle {cycle}: CurrentMark={second.CurrentMark}");
                if (second.CurrentMark > 8)
                {
                    second.CurrentMark.ShouldBe(12);
                    second.IncludesSkipping.ShouldBeTrue();

                    var persisted = await scalar(
                        $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
                    persisted.ShouldBe(12);
                    return;
                }
            }

            throw new ShouldAssertException(
                "The conjoined-tenancy store's high water mark never skipped the dead gap despite the cap having long expired");
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task running_daemon_resumed_after_the_gap_formed_skips_within_the_cap()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            opts.Projections.SkipStaleGapsDespiteLiveTransactionsAfter = 500.Milliseconds();
            opts.Projections.Add(new Bug4953GapViewProjection(), ProjectionLifecycle.Async);
        });
        theStore.EnsureStorageExists(typeof(IEvent));

        var listener = await startIdleAdvisoryLockSession(4953005);
        try
        {
            await appendEvents(8);
            await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 8)");

            // Gap forms while no daemon is running — the resumed daemon's detector never sees the
            // store without the gap
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

            using var daemon = await StartDaemon();

            // threshold + cap total 750ms; the poll cadence adds ~1s slop per safe-zone pass. 10s of
            // no progress means the cap never fired.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            long persisted = 8;
            while (DateTimeOffset.UtcNow < deadline)
            {
                persisted = await scalar(
                    $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");
                if (persisted >= 12)
                {
                    break;
                }

                await Task.Delay(250, TestContext.Current.CancellationToken);
            }

            _output.WriteLine($"Persisted high water after resume: {persisted}");
            persisted.ShouldBe(12);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }
}
