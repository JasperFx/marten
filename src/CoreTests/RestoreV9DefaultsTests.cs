using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
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
    public void the_side_effect_gate_is_on_by_default_in_jasperfx_3()
    {
        new GateProjection().GateSideEffectsBehindPriorVersion.ShouldBeTrue();
    }

    [Fact]
    public void restores_the_side_effect_gate_off_for_registered_projections_when_the_store_is_built()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();
        var projection = new GateProjection();
        opts.Projections.Add(projection, ProjectionLifecycle.Async);

        projection.GateSideEffectsBehindPriorVersion.ShouldBeTrue(); // not until the store is built

        opts.ApplyConfiguration();

        projection.GateSideEffectsBehindPriorVersion.ShouldBeFalse();
    }

    [Fact]
    public void leaves_the_gate_on_without_the_restore()
    {
        var opts = new StoreOptions();
        var projection = new GateProjection();
        opts.Projections.Add(projection, ProjectionLifecycle.Async);

        opts.ApplyConfiguration();

        projection.GateSideEffectsBehindPriorVersion.ShouldBeTrue();
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

    public record GateStarted(System.Guid Id);

    public class GateDoc
    {
        public System.Guid Id { get; set; }
    }

    public partial class GateProjection: SingleStreamProjection<GateDoc, System.Guid>
    {
        public void Apply(GateDoc doc, GateStarted _){}
    }
}
