using JasperFx.Events;
using Marten.Events;
using Shouldly;
using Xunit;

namespace CoreTests.Events;

// #4686 — EventGraph implements JasperFx.Events.IEventStoreInstrumentation (shipped in
// JasperFx 2.9.0, jasperfx#424). Both the legacy EnableExtendedProgressionTracking property
// and the storage-agnostic ExtendedProgressionEnabled name on IEventStoreInstrumentation must
// alias the same underlying field, so Wolverine.CritterWatch.Marten can flip the toggle via
// the abstraction without breaking existing code that sets EnableExtendedProgressionTracking
// directly.
public class EventGraph_IEventStoreInstrumentation
{
    private static EventGraph BuildEventGraph()
    {
        // EventGraph requires a StoreOptions to wire up its registry, but a default
        // StoreOptions is sufficient here -- no connection or schema interaction.
        return new EventGraph(new Marten.StoreOptions());
    }

    [Fact]
    public void implements_event_store_instrumentation()
    {
        BuildEventGraph().ShouldBeAssignableTo<IEventStoreInstrumentation>();
    }

    [Fact]
    public void default_is_disabled()
    {
        var events = BuildEventGraph();

        events.EnableExtendedProgressionTracking.ShouldBeFalse();
        events.ExtendedProgressionEnabled.ShouldBeFalse();
        ((IEventStoreInstrumentation)events).ExtendedProgressionEnabled.ShouldBeFalse();
    }

    [Fact]
    public void enabling_via_legacy_name_flows_to_interface_property()
    {
        var events = BuildEventGraph();
        var instrumentation = (IEventStoreInstrumentation)events;

        events.EnableExtendedProgressionTracking = true;

        events.ExtendedProgressionEnabled.ShouldBeTrue();
        instrumentation.ExtendedProgressionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void enabling_via_interface_flows_to_legacy_name()
    {
        var events = BuildEventGraph();
        var instrumentation = (IEventStoreInstrumentation)events;

        instrumentation.ExtendedProgressionEnabled = true;

        events.EnableExtendedProgressionTracking.ShouldBeTrue();
        events.ExtendedProgressionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void disabling_via_either_surface_unwinds_the_other()
    {
        var events = BuildEventGraph();
        var instrumentation = (IEventStoreInstrumentation)events;

        events.EnableExtendedProgressionTracking = true;
        instrumentation.ExtendedProgressionEnabled = false;

        events.EnableExtendedProgressionTracking.ShouldBeFalse();
        events.ExtendedProgressionEnabled.ShouldBeFalse();
    }
}
