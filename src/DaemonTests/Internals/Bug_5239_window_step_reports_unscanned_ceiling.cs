using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.MultiTenancy;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace DaemonTests.Internals;

public class Bug_5239_window_step_reports_unscanned_ceiling: OneOffConfigurationsContext
{
    private const long WindowSize = 10_000;

    /// <summary>
    /// #5239: the window-step walk scans one 10,000-wide window at a time, but computed the page
    /// ceiling against the full high-water mark instead of the window it actually scanned. The
    /// consumer writes that ceiling as durable projection progress (EventRange(floor, page.Ceiling)
    /// -> last_seq_id), so everything between the window ceiling and the high-water mark was skipped
    /// permanently — no exception, no dead letter, and a shard reporting itself as caught up.
    ///
    /// The trigger is the ordinary case rather than an edge case: the window is 10,000 sequence
    /// numbers wide, the batch size is 500, and this strategy exists precisely because matching
    /// events are sparse, so "returned fewer than BatchSize events" — the branch that took
    /// highWaterMark — is the expected outcome.
    /// </summary>
    [Fact]
    public async Task window_step_ceiling_never_exceeds_the_window_it_scanned()
    {
        var (highWater, loader) = await arrangeSparseEventsAcrossTwoWindowsAsync();

        var request = requestFrom(0, highWater);

        var page = await loader.LoadWithWindowStepAsync(request, CancellationToken.None);

        // Only the first window was scanned, so only the five events inside it can be reported...
        page.Count.ShouldBe(5);

        // ...and the ceiling must say so. Pre-fix this was highWater (11005), which handed the
        // daemon progress over 6,000 sequence numbers of events it had never looked at.
        page.Ceiling.ShouldBe(WindowSize);
    }

    /// <summary>
    /// The other half of the contract: capping the ceiling is only correct if the walk actually
    /// comes back for the rest. Feeding the reported ceiling back as the next floor — which is what
    /// the daemon does — must surface the events above the first window.
    /// </summary>
    [Fact]
    public async Task the_next_pass_picks_up_the_events_above_the_first_window()
    {
        var (highWater, loader) = await arrangeSparseEventsAcrossTwoWindowsAsync();

        var first = await loader.LoadWithWindowStepAsync(requestFrom(0, highWater), CancellationToken.None);
        var second = await loader.LoadWithWindowStepAsync(requestFrom(first.Ceiling, highWater), CancellationToken.None);

        second.Count.ShouldBe(5);
        second.Ceiling.ShouldBe(highWater);

        // Between the two passes every matching event is accounted for, which is the property
        // #5239 broke: pre-fix the first page claimed highWater and these five were never loaded.
        (first.Count + second.Count).ShouldBe(10);
    }

    /// <summary>
    /// A walk that genuinely scans every window up to the high-water mark and finds nothing may
    /// still report highWater — that branch was correct and must stay that way, or the shard stalls
    /// re-reading empty windows forever.
    /// </summary>
    [Fact]
    public async Task exhausting_the_whole_range_still_reports_the_high_water_mark()
    {
        await resetEventsAsync();

        // Events exist, but none of the type this loader filters on, so every window comes back empty.
        theSession.Events.StartStream<Letters>(Guid.NewGuid(), new MTBEvent(), new MTBEvent());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        await setSequenceAsync(11_000);

        var loader = loaderForMTAEvents();
        var page = await loader.LoadWithWindowStepAsync(requestFrom(0, 11_005), CancellationToken.None);

        page.Count.ShouldBe(0);
        page.Ceiling.ShouldBe(11_005);
    }

    /// <summary>
    /// Five matching events low in the sequence, then a jump past the first 10,000-wide window and
    /// five more above it. Returns the high-water mark and a loader filtered to the matching type.
    /// </summary>
    private async Task<(long HighWater, EventLoader Loader)> arrangeSparseEventsAcrossTwoWindowsAsync()
    {
        await resetEventsAsync();

        theSession.Events.StartStream<Letters>(Guid.NewGuid(), new MTAEvent(), new MTAEvent(), new MTAEvent(),
            new MTAEvent(), new MTAEvent());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Jump the sequence clear of the first window so the next append lands above it.
        await setSequenceAsync(11_000);

        theSession.Events.StartStream<Letters>(Guid.NewGuid(), new MTAEvent(), new MTAEvent(), new MTAEvent(),
            new MTAEvent(), new MTAEvent());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (11_005, loaderForMTAEvents());
    }

    private EventLoader loaderForMTAEvents()
    {
        var filters = new ISqlFragment[] { new EventTypeFilter(theStore.Events, new[] { typeof(MTAEvent) }) };

        // BatchSize well above the five events each window holds, so the page never fills the batch
        // — the partial-page branch is exactly the one #5239 got wrong.
        return new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database,
            new AsyncOptions { BatchSize = 500 }, filters);
    }

    private static EventRequest requestFrom(long floor, long highWater) =>
        new()
        {
            Floor = floor,
            HighWater = highWater,
            BatchSize = 500,
            ErrorOptions = new ErrorHandlingOptions(),
            Runtime = new NulloDaemonRuntime(),
            Name = new ShardName("Letters", "All", 1)
        };

    private async Task resetEventsAsync()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        // DeleteAllEventDataAsync is not required to rewind the sequence, and the assertions here
        // are stated in absolute sequence numbers, so pin it rather than inherit whatever ran before.
        await setSequenceAsync(1, isCalled: false);
    }

    private async Task setSequenceAsync(long value, bool isCalled = true)
    {
        var schema = theStore.Options.Events.DatabaseSchemaName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select setval('{schema}.mt_events_sequence', {value}, {isCalled.ToString().ToLowerInvariant()});";
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await conn.CloseAsync();
    }
}
