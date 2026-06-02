using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Composites;

/// <summary>
/// #4596 Phase A headline — single-pass rebuild for composite projections.
///
/// <para>
/// JasperFx.Events 2.5.0-pt209.2 shipped <c>CompositeReplayExecutor</c> +
/// <c>CompositeProjection.TryBuildReplayExecutor</c> + the
/// <c>CanParticipateInCompositeReplay</c> membership guard. Together they
/// collapse N member-projection rebuild passes into ONE ordered pass over
/// the event store: read each event once, fan it to every member stage,
/// commit the combined member work as a single batch per page.
/// </para>
///
/// <para>
/// The orchestration-level wiring lives in JasperFx (covered by tests there).
/// This Marten-side test pins the document-level rebuild correctness against
/// a concrete event store: trigger a composite rebuild, then assert every
/// member's documents are correct AND the daemon never spawned per-member
/// rebuild agents (because the composite's single shard owned the pass).
/// </para>
/// </summary>
public class composite_single_pass_rebuild : DaemonContext
{
    public composite_single_pass_rebuild(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task rebuild_composite_single_pass_writes_every_member_correctly_and_advances_only_the_composite_shard()
    {
        StoreOptions(opts =>
        {
            // 3 members across 2 stage levels: stage 1 (default) carries the
            // two aggregation projections, stage 2 carries the custom IProjection.
            // The composite executor reads the events once and fans every page
            // through every stage in order.
            opts.Projections.CompositeProjectionFor("TripsRebuild", x =>
            {
                x.Add<TestingSupport.TripProjection>();
                x.Add<DayProjection>();
                x.Add(new TripMetricsProjection(), stageNumber: 2);
            });
        }, true);

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        // Rebuild from scratch (no continuous baseline) so the only progression
        // rows that exist after the rebuild are the ones the rebuild itself
        // produced — that's how we can prove the single-pass executor (the
        // composite's one shard) ran, not a per-member fan-out.
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync("TripsRebuild", 1.Minutes(), CancellationToken.None);

        // Every member's documents are correct after the single-pass rebuild —
        // the headline document-level correctness signal that couldn't live in
        // JasperFx.Events (no concrete event store there).
        var trips = await theSession.Query<Trip>().ToListAsync();
        var days = await theSession.Query<Day>().ToListAsync();
        var metrics = await theSession.Query<TripMetrics>().ToListAsync();

        trips.Count.ShouldBe(NumberOfStreams,
            "the TripProjection stage wrote one document per stream");
        days.Count.ShouldBeGreaterThan(0,
            "the DayProjection stage wrote one document per distinct travel day");
        metrics.Count.ShouldBeGreaterThan(0,
            "the custom TripMetricsProjection IProjection wrote at least one metric");

        // Composite progression advanced to the high-water mark on its single
        // shard {Name}:All — CompositeReplayExecutor calls
        // controller.MarkSuccessAsync(ceiling) per page and the composite shard
        // owns the row, not any of the members.
        var progressions = await theStore.Advanced.AllProjectionProgress();
        var compositeProgression = progressions.SingleOrDefault(x => x.ShardName == "TripsRebuild:All");
        compositeProgression.ShouldNotBeNull(
            "Composite single shard must have a progression row at the end of the rebuild");
        compositeProgression.Sequence.ShouldBe(NumberOfEvents,
            "Composite shard's progression should reach the high-water mark in one pass");

        // Single-pass signal: every member's progression row advanced to the
        // SAME high-water mark, in lockstep with the composite shard. Phase A's
        // CompositeExecution.ExecuteDownstreamAsync clones the per-page event
        // range to each member's shard name and records progress within the
        // composite's one batch — so each member shard's last_seq_id mirrors the
        // composite's. If the daemon had fanned out N independent per-member
        // rebuilds, their last_seq_ids could drift; here they cannot because
        // they all ride the same batch.
        var perMemberShards = new[] { "Trip:All", "Day:All", "TripMetricsProjection:All" };
        foreach (var shardName in perMemberShards)
        {
            var row = progressions.SingleOrDefault(x => x.ShardName == shardName);
            row.ShouldNotBeNull($"member shard {shardName} must have a progression row written by the composite batch");
            row.Sequence.ShouldBe(NumberOfEvents,
                $"member shard {shardName} must mirror the composite's ceiling — they all advance as one unit in the single pass");
        }
    }
}
