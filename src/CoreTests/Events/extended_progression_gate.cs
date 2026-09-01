using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Events;

// #750 regression guard. The async daemon's ExtendedProgressionWriter observer gates on
// IEventStore.ExtendedProgressionEnabled. Marten's DocumentStore must reflect the store setting here
// or the observer silently never fires and the extended progression columns (heartbeat /
// agent_status / pause_reason / running_on_node) stay NULL -- exactly the "a feature that was built
// and never connected" failure #750 reported. Since #5310 the store default is true, so the failure
// #750 describes now lives on the opt-OUT side: a store that turns tracking off must be reported as
// off, or the observer fires for a deployment that asked it not to.
public class extended_progression_gate
{
    // #5310: the default is true again. What #750 is actually about is that this property must TRACK
    // the store option rather than being hardcoded, so the guard is that both polarities are reported
    // faithfully -- not that either one of them is the default.
    [Fact]
    public void event_store_reports_extended_progression_enabled_by_default()
    {
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        ((IEventStore)store).ExtendedProgressionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void event_store_reflects_the_extended_progression_opt_out()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = false;
        });

        ((IEventStore)store).ExtendedProgressionEnabled.ShouldBeFalse();
    }

    [Fact]
    public void event_store_reflects_the_extended_progression_opt_in()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        ((IEventStore)store).ExtendedProgressionEnabled.ShouldBeTrue();
    }
}
