using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Services;
using Marten.Testing;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Internals;

public class GapDetectorTest: DaemonContext
{
    private readonly GapDetector theGapDetector;
    private readonly ISingleQueryRunner _runner;

    public GapDetectorTest(ITestOutputHelper output) : base(output)
    {
        theStore.EnsureStorageExists(typeof(IEvent));

        theGapDetector = new GapDetector(theStore.Events);
        _runner = (ISingleQueryRunner)theStore.Tenancy.Default.Database;
    }

    [Fact]
    public async Task detect_first_gap()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();
        await deleteEvents(NumberOfEvents - 100, NumberOfEvents - 50);

        var current = await _runner.Query(theGapDetector, CancellationToken.None);

        current.ShouldBe(NumberOfEvents - 101);
    }

    [Fact]
    public async Task detect_gap_if_gap_is_right_after_start()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();
        await deleteEvents(NumberOfEvents - 100, NumberOfEvents - 50);
        var current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);
        theGapDetector.Start = current.Value;

        current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);

        current.ShouldBe(NumberOfEvents - 101);
    }

    [Fact]
    public async Task get_max_seq_id_if_no_gap()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);

        current.ShouldBe(NumberOfEvents);
    }

    [Fact]
    public async Task get_max_seq_id_if_start_is_max_seq_id()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();
        theGapDetector.Start = NumberOfEvents;

        var current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);

        current.ShouldBe(NumberOfEvents);
    }

    [Fact]
    public async Task normal_path_holds_at_start_before_a_leading_gap_when_start_is_a_hole()
    {
        // #4964: the silent-skip blind spot. Delete a contiguous block so Start itself is a hole and the
        // first committed sequence above it sits beyond Start + 1. The interior-gap query cannot see this
        // (there is no visible row AT Start to pair with the first row above the gap), so without the hold
        // it falls through to max(seq_id) and the Normal high-water mark crosses the invisible events with
        // no trace. With HoldBeforeLeadingGap on (the Normal path), it must hold at Start instead.
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var hole = NumberOfEvents / 2;
        await deleteEvents(hole, hole + 1, hole + 2, hole + 3, hole + 4, hole + 5);

        theGapDetector.Start = hole; // Start lands ON a hole
        theGapDetector.HoldBeforeLeadingGap = true; // Normal detection path

        var current = await _runner.Query(theGapDetector, CancellationToken.None);

        current.ShouldBe(hole);
    }

    [Fact]
    public async Task safe_zone_path_still_skips_a_leading_gap_forward_to_max()
    {
        // The SafeZone counterpart: with HoldBeforeLeadingGap off, the same hole state must keep the
        // original skip-forward behavior so the SafeZone path can advance past a permanent hole (and
        // record the skip). This guards against the #4964 hold leaking into the SafeZone path.
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var hole = NumberOfEvents / 2;
        await deleteEvents(hole, hole + 1, hole + 2, hole + 3, hole + 4, hole + 5);

        theGapDetector.Start = hole;
        theGapDetector.HoldBeforeLeadingGap = false; // SafeZone path

        var current = await _runner.Query(theGapDetector, CancellationToken.None);

        current.ShouldBe(NumberOfEvents);
    }

    protected async Task deleteEvents(params long[] ids)
    {
        await using var conn = theStore.CreateConnection();
        await conn.OpenAsync();

        await conn
            .CreateCommand($"delete from {theStore.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(:ids)")
            .With("ids", ids, NpgsqlDbType.Bigint | NpgsqlDbType.Array)
            .ExecuteNonQueryAsync();
    }
}
