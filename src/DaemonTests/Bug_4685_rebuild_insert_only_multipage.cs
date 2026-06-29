using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public record Bug4685Incremented();

public class Bug4685Counter
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class Bug4685CounterProjection: SingleStreamProjection<Bug4685Counter, Guid>
{
    public Bug4685CounterProjection()
    {
        Name = "Bug4685Counter";
        Options.TeardownDataOnRebuild = true;

        // Force a single stream's events to span multiple rebuild pages: 30 events / BatchSize 10
        // => 3 pages => 3 ProjectionUpdateBatch flushes that each re-store this stream's aggregate.
        Options.BatchSize = 10;
    }

    public void Apply(Bug4685Incremented _, Bug4685Counter counter) => counter.Count++;
}

/// <summary>
/// #4685 PR 2 — proving the blocker. Marten's rebuild flushes page-by-page (one
/// <c>ProjectionUpdateBatch</c> per <c>EventPage</c>, see
/// <c>GroupedProjectionExecution.processRangeAsync</c>), and the aggregate cache spans pages,
/// so a stream whose events span more than one page has its aggregate written once per page.
/// <para>
/// With the default UPSERT/OVERWRITE routing that is correct (later pages overwrite). The
/// EXPERIMENTAL <c>ProjectionOptions.RebuildWithInsertOnly</c> routing (the INSERT-only seam the
/// BulkWriter binary <c>COPY</c> flush in PR 3 needs) instead INSERTs each write — so the second
/// page that touches the stream's aggregate throws <c>23505 / DocumentAlreadyExistsException</c>.
/// The INSERT-only premise only holds once CritterWatch#208 Phase 4 (deferred flush via
/// identity-map accumulation) guarantees each aggregate is written exactly once. These two tests
/// pin that boundary; when Phase 4 lands, the second test's assertion flips to expect success.
/// </para>
/// </summary>
public class Bug_4685_rebuild_insert_only_multipage: DaemonContext
{
    public Bug_4685_rebuild_insert_only_multipage(ITestOutputHelper output): base(output)
    {
    }

    private async Task<Guid> seedSingleStreamSpanningPagesAsync()
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < 30; i++)
        {
            session.Events.Append(streamId, new Bug4685Incremented());
        }

        await session.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public async Task default_upsert_routing_rebuilds_a_multipage_stream_correctly()
    {
        // Default behavior — RebuildWithInsertOnly is off. No behavior change from PR 2.
        StoreOptions(x => x.Projections.Add(new Bug4685CounterProjection(), ProjectionLifecycle.Async));

        var streamId = await seedSingleStreamSpanningPagesAsync();

        var daemon = await StartDaemon();
        await daemon.RebuildProjectionAsync("Bug4685Counter", CancellationToken.None);

        await using var query = theStore.QuerySession();
        var counter = await query.LoadAsync<Bug4685Counter>(streamId);
        counter.ShouldNotBeNull();
        counter.Count.ShouldBe(30);
    }

    [Fact]
    public async Task insert_only_routing_collides_on_a_multipage_stream_until_phase_4()
    {
        // Turn on the experimental INSERT-only rebuild routing. This is the blocker proof:
        // the page-batched rebuild re-stores the stream's aggregate on the second page,
        // and INSERT of an already-present id fails with 23505.
        StoreOptions(x =>
        {
            x.Projections.Add(new Bug4685CounterProjection(), ProjectionLifecycle.Async);
            x.Projections.RebuildWithInsertOnly = true;
        });

        var streamId = await seedSingleStreamSpanningPagesAsync();

        var daemon = await StartDaemon();

        // The daemon records the 23505 / DocumentAlreadyExistsException internally and stops the
        // shard rather than always rethrowing to the caller, so the failure manifests as either a
        // thrown exception OR a silently incomplete rebuild. Capture both.
        var thrown = await Record.ExceptionAsync(() =>
            daemon.RebuildProjectionAsync("Bug4685Counter", CancellationToken.None));

        await using var query = theStore.QuerySession();
        var counter = await query.LoadAsync<Bug4685Counter>(streamId);

        // The correct, post-Phase-4 outcome is Count == 30. The INSERT-only routing cannot reach
        // it on a multipage stream: the rebuild either faults or leaves the aggregate incomplete.
        var rebuiltCorrectly = thrown is null && counter is { Count: 30 };
        rebuiltCorrectly.ShouldBeFalse(
            "INSERT-only rebuild routing must NOT correctly rebuild a multipage stream until " +
            "CritterWatch#208 Phase 4 (single-flush) lands; this pins the duplicate-key blocker. " +
            $"thrown={thrown?.GetType().Name ?? "none"}, count={counter?.Count.ToString() ?? "null"}");
    }
}
