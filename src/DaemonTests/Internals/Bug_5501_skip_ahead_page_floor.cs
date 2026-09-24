using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.MultiTenancy;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Internals;

/// <summary>
/// #5501 — the skip-ahead loader runs its query from an ADJUSTED floor (just before the first event
/// matching the type filter), and it was reporting that adjusted floor as the page's floor. JasperFx
/// turns <c>page.Floor</c> into <c>EventRange.SequenceFloor</c>, which is the key of the store's
/// optimistic progression write — <c>set last_seq_id = ceiling where last_seq_id = floor</c>. The
/// stored <c>last_seq_id</c> is still the floor that was REQUESTED, so an adjusted floor matches no
/// rows and the progression silently does not move.
///
/// <para>
/// When the requested floor is 0 it is worse than a no-op: the adjusted floor is above 0, so
/// <c>ProjectionBatch.RecordProgress</c> queues an UPDATE rather than an INSERT, and there is no row
/// to update at all.
/// </para>
///
/// <para>
/// The window-step loader in the same file has always kept the original floor for exactly this
/// reason. Only the skip-ahead path drifted, and it is reachable only when a normal load times out
/// on a shard carrying an event-type filter, which is why it went unnoticed: the existing
/// <c>Bug_4744</c> and <c>Bug_5277</c> tests drive this path but assert only on <c>page.Count</c>.
/// </para>
/// </summary>
public class Bug_5501_skip_ahead_page_floor: OneOffConfigurationsContext
{
    // Events land at seq 1..4; only the MTCEvent at seq 4 matches the filter, so the probe adjusts
    // the query floor to 3 and the bug shows up as a page floor of 3 for any requested floor below it.
    private async Task<EventLoader> loaderForOneMatchAtSequenceFour()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        theSession.Events.StartStream<Letters>(Guid.NewGuid(),
            new MTAEvent(), new MTBEvent(), new MTBEvent(), new MTCEvent());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database,
            new AsyncOptions(), [new EventTypeFilter(theStore.Events, [typeof(MTCEvent)])]);
    }

    private static EventRequest RequestFrom(long floor) => new()
    {
        Floor = floor,
        HighWater = 4,
        BatchSize = 100,
        ErrorOptions = new ErrorHandlingOptions(),
        Runtime = new NulloDaemonRuntime(),
        Name = new ShardName("Letters", "All", 1)
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task page_floor_is_the_requested_floor(long floor)
    {
        var loader = await loaderForOneMatchAtSequenceFour();

        var page = await loader.LoadWithSkipAheadAsync(RequestFrom(floor), CancellationToken.None);

        // Pre-fix this is 3 — the adjusted floor — for every one of these.
        page.Floor.ShouldBe(floor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task skipping_ahead_still_returns_the_matching_events(long floor)
    {
        var loader = await loaderForOneMatchAtSequenceFour();

        var page = await loader.LoadWithSkipAheadAsync(RequestFrom(floor), CancellationToken.None);

        // The adjusted floor still drives the QUERY, so the skip is intact and the scan does not
        // walk the three non-matching events.
        page.Count.ShouldBe(1);
        page.Single().Sequence.ShouldBe(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task the_ceiling_is_unchanged_by_reporting_the_requested_floor(long floor)
    {
        var loader = await loaderForOneMatchAtSequenceFour();

        var page = await loader.LoadWithSkipAheadAsync(RequestFrom(floor), CancellationToken.None);

        // EventPage.CalculateCeiling reads Floor only to sanity-check a LastObservedSequence in the
        // all-rows-skipped branch, and every row this query could read is above the adjusted floor,
        // which is above the requested one. Lowering the reported floor must not move the ceiling.
        page.Ceiling.ShouldBe(4);
    }

    [Fact]
    public async Task a_page_with_no_match_at_all_also_reports_the_requested_floor()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        theSession.Events.StartStream<Letters>(Guid.NewGuid(), new MTAEvent(), new MTBEvent());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var loader = new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database,
            new AsyncOptions(), [new EventTypeFilter(theStore.Events, [typeof(MTCEvent)])]);

        var page = await loader.LoadWithSkipAheadAsync(RequestFrom(1), CancellationToken.None);

        page.Count.ShouldBe(0);
        page.Floor.ShouldBe(1);
    }
}
