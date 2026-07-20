using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Events;

// #750 regression guard. The async daemon's ExtendedProgressionWriter observer gates on
// IEventStore.ExtendedProgressionEnabled, whose interface default is false. Marten's DocumentStore
// must reflect the store opt-in here or the observer silently never fires and the extended
// progression columns (heartbeat / agent_status / pause_reason / running_on_node) stay NULL --
// exactly the "a feature that was built and never connected" failure #750 reported.
public class extended_progression_gate
{
    [Fact]
    public void event_store_reports_extended_progression_disabled_by_default()
    {
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
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
