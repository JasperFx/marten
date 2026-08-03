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
/// #5108 §2: recovery from a race-heavy field of dead gaps used to be a serial grind — the skip
/// advanced to the next gap edge, so N gaps cost N detection cycles and each one re-paid
/// StaleSequenceThreshold from a fresh observation. The reporter measured a mark crawling 38 → 426
/// over 21 seconds against ~50 gaps in 489 sequences, with the cost growing linearly in write
/// contention.
///
/// One liveness verdict already proves the whole span dead: every sequence number at or below the
/// ceiling recorded when the gap was first observed was handed out at or before that moment (nextval
/// runs inside the reserving transaction, so its xact_start cannot postdate it), and the probe
/// establishes that no transaction from before the observation is still running.
/// </summary>
public class Bug_5108_dead_gap_span_clears_in_one_cycle: DaemonContext
{
    public Bug_5108_dead_gap_span_clears_in_one_cycle(ITestOutputHelper output): base(output)
    {
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    private async Task<NpgsqlConnection> openConnection()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task appendEvents(int count)
    {
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < count; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new Bug5108SpanEvent(i + 1));
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

    [Fact]
    public async Task a_field_of_dead_gaps_is_cleared_by_a_single_skip()
    {
        StoreOptions(opts => opts.Projections.StaleSequenceThreshold = 500.Milliseconds());
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendEvents(40);
        // Park the mark directly under the first hole so the very first DetectInSafeZone call sees the
        // gap pinned rather than spending a cycle on an ordinary contiguous advance
        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 6)");

        // A race-heavy field: many rolled-back appends scattered above the mark. Deleting the rows
        // leaves exactly what a rolled-back SaveChanges leaves behind — allocated sequence numbers
        // that will never commit.
        await execute($"delete from {Schema}.mt_events where seq_id in (7, 9, 12, 13, 18, 25, 26, 31, 37)");

        var ceiling = await scalar($"select coalesce(max(seq_id), 0) from {Schema}.mt_events");
        ceiling.ShouldBe(40);

        var detector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            NullLogger.Instance);

        // First sighting holds — the gap has to be stuck past the threshold before anything is skipped
        var held = await detector.DetectInSafeZone(CancellationToken.None);
        held.CurrentMark.ShouldBe(6);

        await Task.Delay(700, TestContext.Current.CancellationToken);

        // ONE cycle clears all nine gaps, not one gap per cycle
        var skipped = await detector.DetectInSafeZone(CancellationToken.None);
        skipped.CurrentMark.ShouldBe(40);
        skipped.IncludesSkipping.ShouldBeTrue();

        (await scalar(
                $"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'"))
            .ShouldBe(40);
    }
}

public record Bug5108SpanEvent(int Number);
