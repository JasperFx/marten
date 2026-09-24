#nullable enable
using System.Linq;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using DaemonTests.Internals;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using Marten.Events.Projections;
using Shouldly;
using Xunit;

namespace DaemonTests.Composites;

/// <summary>
/// #5502 — a composite projection is the shape that actually exercises the bug in production.
/// <c>CompositeExecution</c> opens ONE batch for the parent (whose
/// <c>StartProjectionBatchAsync</c> queues the parent's progression first, so it lands at index 0),
/// and then <c>ExecutionStage.ExecuteDownstreamAsync</c> calls
/// <c>cloned.ActiveBatch.RecordProgress(cloned)</c> for every member into that SAME batch. So a
/// composite with N members puts N+1 progression operations on one page — the parent's was checked
/// and every member's was skipped by the <c>NoDataReturnedCall</c> continue.
///
/// <para>
/// The risk in making the check effective is false positives, so this covers the happy path end to
/// end: a composite that is behaving must produce no warnings at all while every member's
/// progression advances in lockstep with the parent's.
/// </para>
/// </summary>
public class composite_progression_is_checked_for_every_member: DaemonContext
{
    private readonly RecordingLogger theLogger = new();

    public composite_progression_is_checked_for_every_member(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task a_healthy_composite_run_reports_no_progression_problems()
    {
        StoreOptions(opts =>
        {
            opts.DotNetLogger = theLogger;

            opts.Projections.CompositeProjectionFor("Trips", x =>
            {
                x.Add<TestingSupport.TripProjection>();
                x.Add<DayProjection>();
                x.Add(new TripMetricsProjection());
            });
        }, true);

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        // The structural claim: the composite really does keep a progression row per member as well
        // as for the parent shard, which is why more than one lands in a single batch.
        var progressions = await theStore.Advanced.AllProjectionProgress(
            token: TestContext.Current.CancellationToken);

        var relevant = progressions.Where(x => x.ShardName != "HighWaterMark").ToArray();

        // The parent plus one per member: four progression operations, all in the one batch
        // CompositeExecution opened. Only the parent's sat at index 0, so pre-fix three of these
        // four were never checked at all.
        relevant.Select(x => x.ShardName).ShouldBe(
            new[] { "Trips:All", "Trip:All", "Day:All", "TripMetricsProjection:All" }, ignoreOrder: true);

        // Every one of them advanced, parent and members alike...
        foreach (var progression in relevant)
        {
            progression.Sequence.ShouldBe(NumberOfEvents);
        }

        // ...and nothing was reported, which is the part that matters: the newly effective check
        // must not cry wolf over a composite that is working correctly.
        theLogger.Warnings.ShouldBeEmpty();
    }
}
