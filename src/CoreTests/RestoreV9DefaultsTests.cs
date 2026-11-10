using Marten;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     Trust-gate for <see cref="StoreOptions.RestoreV9Defaults"/>, the single opt-out for the
///     Marten 10.0 default flips. Each flipped default gets a test that sets it to the 10.0 value
///     and asserts the call restores the 9.x value. A node that flips a default adds its test here.
/// </summary>
public class RestoreV9DefaultsTests
{
    [Fact]
    public void restores_extended_progression_tracking_to_the_v9_value()
    {
        var opts = new StoreOptions();
        opts.Events.EnableExtendedProgressionTracking = true;

        opts.RestoreV9Defaults();

        opts.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    [Fact]
    public void does_not_touch_the_v9_defaults_that_restore_v8_defaults_owns()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.DisableNpgsqlLogging.ShouldBeTrue();
        opts.Events.UseIdentityMapForAggregates.ShouldBeTrue();
        opts.Events.EnableBigIntEvents.ShouldBeTrue();
    }

    [Fact]
    public void overrides_made_after_the_call_stick()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();
        opts.Events.EnableExtendedProgressionTracking = true;

        opts.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }
}
