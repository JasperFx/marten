using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace CoreTests;

public class constructing_projection_coordinator_when_async_disabled
{
    // Regression: a ProjectionCoordinator can be *constructed* (but never started) while the
    // async daemon is Disabled — e.g. Wolverine's ancillary-store integration directly news up
    // IProjectionCoordinator<T> for stores whose subscription distribution it manages itself
    // (AncillaryWolverineOptionsMartenExtensions). The #4516 dedupe lifted the coordinator loop
    // into JasperFx.Events' ProjectionCoordinatorBase, whose ctor rejects a null distributor;
    // BuildDistributor previously returned null in Disabled mode, so construction threw
    // ArgumentNullException(distributor). It now returns a no-op distributor instead.
    [Fact]
    public void can_construct_when_async_mode_is_disabled()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            // AsyncMode is left at its default of DaemonMode.Disabled
        });

        // Previously: System.ArgumentNullException : Value cannot be null. (Parameter 'distributor')
        var coordinator = new ProjectionCoordinator(store, NullLogger<ProjectionCoordinator>.Instance);
        coordinator.Distributor.ShouldNotBeNull();
    }
}
