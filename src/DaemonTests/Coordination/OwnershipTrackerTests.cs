using System;
using JasperFx.Core;
using Marten.Events.Daemon.Coordination;
using Shouldly;
using Xunit;

namespace DaemonTests.Coordination;

public class OwnershipTrackerTests
{
    private static readonly TimeSpan Threshold = 2.Minutes();
    private static readonly TimeSpan Repeat = 5.Minutes();

    private readonly AdjustableTimeProvider theTime = new();
    private readonly OwnershipTracker theTracker;

    public OwnershipTrackerTests()
    {
        theTracker = new OwnershipTracker(theTime);
    }

    private OwnershipReport record(int owned, int denied, int total, TimeSpan? threshold = null)
    {
        return theTracker.Record(owned, denied, total, threshold ?? Threshold, Repeat);
    }

    [Fact]
    public void reports_the_tally_as_changed_on_the_very_first_cycle()
    {
        record(3, 0, 3).TallyChanged.ShouldBeTrue();
    }

    [Fact]
    public void is_quiet_while_the_tally_is_unchanged()
    {
        record(3, 0, 3);

        record(3, 0, 3).TallyChanged.ShouldBeFalse();
        record(3, 0, 3).TallyChanged.ShouldBeFalse();
    }

    [Fact]
    public void reports_a_change_when_databases_are_lost()
    {
        record(3, 0, 3);

        record(1, 2, 3).TallyChanged.ShouldBeTrue();
    }

    [Fact]
    public void reports_a_change_when_only_the_denied_count_moves()
    {
        // Same number owned, but the rest of the cluster's shape changed underneath us --
        // worth a line, because it means sets appeared or disappeared.
        record(1, 1, 2);

        record(1, 2, 3).TallyChanged.ShouldBeTrue();
    }

    [Fact]
    public void does_not_warn_before_the_threshold_has_elapsed()
    {
        record(0, 33, 33).ShouldWarn.ShouldBeFalse();

        theTime.Advance(119.Seconds());

        record(0, 33, 33).ShouldWarn.ShouldBeFalse();
    }

    [Fact]
    public void warns_once_the_threshold_has_elapsed()
    {
        record(0, 33, 33);

        theTime.Advance(2.Minutes()).Advance(1.Seconds());

        var report = record(0, 33, 33);
        report.ShouldWarn.ShouldBeTrue();
        report.OwnedNothingFor.ShouldBeGreaterThanOrEqualTo(2.Minutes());
    }

    [Fact]
    public void throttles_the_warning_to_the_repeat_time()
    {
        record(0, 33, 33);
        theTime.Advance(3.Minutes());
        record(0, 33, 33).ShouldWarn.ShouldBeTrue();

        theTime.Advance(4.Minutes());
        record(0, 33, 33).ShouldWarn.ShouldBeFalse();

        theTime.Advance(2.Minutes());
        record(0, 33, 33).ShouldWarn.ShouldBeTrue();
    }

    [Fact]
    public void never_warns_when_the_threshold_is_disabled()
    {
        // Calling Record directly -- the helper's null means "use the default threshold"
        theTracker.Record(0, 33, 33, null, Repeat);

        theTime.Advance(2.Hours());

        theTracker.Record(0, 33, 33, null, Repeat).ShouldWarn.ShouldBeFalse();
    }

    [Fact]
    public void does_not_warn_when_there_is_simply_nothing_to_own()
    {
        // A store with no asynchronous projections at all owns zero sets forever, and that is
        // not a node that has been locked out of its work.
        record(0, 0, 0);

        theTime.Advance(2.Hours());

        record(0, 0, 0).ShouldWarn.ShouldBeFalse();
    }

    [Fact]
    public void recovering_ownership_resets_the_clock_and_the_throttle()
    {
        record(0, 33, 33);
        theTime.Advance(3.Minutes());
        record(0, 33, 33).ShouldWarn.ShouldBeTrue();

        // The zombie sessions time out and this node takes the databases over
        theTime.Advance(1.Minutes());
        record(33, 0, 33).ShouldWarn.ShouldBeFalse();

        // ...and then loses them again. The threshold has to be served afresh rather than
        // firing immediately off the previous outage's clock.
        theTime.Advance(1.Minutes());
        record(0, 33, 33).ShouldWarn.ShouldBeFalse();

        theTime.Advance(3.Minutes());
        record(0, 33, 33).ShouldWarn.ShouldBeTrue();
    }

    [Fact]
    public void partial_ownership_is_not_a_warning()
    {
        // Owning even one set means the coordinator is alive and acquiring. This is the
        // 19:48 point of the incident, where the replacement node had taken 1 of 33.
        record(1, 32, 33);

        theTime.Advance(2.Hours());

        record(1, 32, 33).ShouldWarn.ShouldBeFalse();
    }

    internal class AdjustableTimeProvider: TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 28, 19, 33, 0, TimeSpan.Zero);

        public AdjustableTimeProvider Advance(TimeSpan span)
        {
            _now = _now.Add(span);
            return this;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }
}
