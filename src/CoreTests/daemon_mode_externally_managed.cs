using System;
using System.IO;
using System.Linq;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
// JFx.Events 2.0 introduced its own IProjectionCoordinator interface; alias to the
// Marten-specific one these tests assert on (same trick as DaemonTests service_registrations).
using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;
using ProjectionCoordinator = Marten.Events.Daemon.Coordination.ProjectionCoordinator;

namespace CoreTests;

// jasperfx#490: DaemonMode.ExternallyManaged = an external system (e.g. Wolverine's managed
// event-subscription distribution) executes the async projections. The store itself hosts
// no daemon coordination — the same runtime posture as Disabled — but it must NOT warn
// that async projections will never run, because the external host runs them.
public class daemon_mode_externally_managed
{
    [Fact]
    public void async_mode_is_publicly_settable_through_projection_options()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            // This is the integration path: Wolverine (or any external host) sets the mode
            // on StoreOptions directly rather than calling AddAsyncDaemon()
            opts.Projections.AsyncMode = DaemonMode.ExternallyManaged;
        });

        store.Options.Projections.AsyncMode.ShouldBe(DaemonMode.ExternallyManaged);
    }

    [Fact]
    public void does_not_warn_about_disabled_daemon_when_externally_managed()
    {
        var output = buildStoreWithAsyncProjectionCapturingConsole(DaemonMode.ExternallyManaged);

        output.ShouldNotContain("Warning: The async daemon is disabled.");
        output.ShouldNotContain("will not be executed without the async daemon enabled");
    }

    [Fact]
    public void still_warns_about_disabled_daemon_when_disabled()
    {
        var output = buildStoreWithAsyncProjectionCapturingConsole(DaemonMode.Disabled);

        output.ShouldContain("Warning: The async daemon is disabled.");
        output.ShouldContain("will not be executed without the async daemon enabled");
    }

    [Fact]
    public void setting_externally_managed_without_add_async_daemon_registers_no_coordination()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DisableNpgsqlLogging = true;
                    opts.Projections.AsyncMode = DaemonMode.ExternallyManaged;
                });
            }).Build();

        host.Services.GetService<IProjectionCoordinator>().ShouldBeNull();
        host.Services.GetServices<IHostedService>().OfType<ProjectionCoordinator>().Any().ShouldBeFalse();
    }

    [Fact]
    public void add_async_daemon_with_externally_managed_registers_no_coordination()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DisableNpgsqlLogging = true;
                }).AddAsyncDaemon(DaemonMode.ExternallyManaged);
            }).Build();

        host.Services.GetService<IProjectionCoordinator>().ShouldBeNull();
        host.Services.GetService<JasperFx.Events.Daemon.IProjectionCoordinator>().ShouldBeNull();
        host.Services.GetServices<IHostedService>().OfType<ProjectionCoordinator>().Any().ShouldBeFalse();

        // ... but the mode itself is still carried honestly on the store options
        var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
        store.Options.Projections.AsyncMode.ShouldBe(DaemonMode.ExternallyManaged);
    }

    [Fact]
    public void add_async_daemon_with_solo_is_unaffected()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DisableNpgsqlLogging = true;
                }).AddAsyncDaemon(DaemonMode.Solo);
            }).Build();

        var coordinator = host.Services.GetService<IProjectionCoordinator>()
            .ShouldBeOfType<ProjectionCoordinator>();
        coordinator.Mode.ShouldBe(DaemonMode.Solo);

        host.Services.GetServices<IHostedService>().OfType<ProjectionCoordinator>().Any().ShouldBeTrue();
    }

    [Fact]
    public void add_async_daemon_with_hot_cold_is_unaffected()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DisableNpgsqlLogging = true;
                }).AddAsyncDaemon(DaemonMode.HotCold);
            }).Build();

        var coordinator = host.Services.GetService<IProjectionCoordinator>()
            .ShouldBeOfType<ProjectionCoordinator>();
        coordinator.Mode.ShouldBe(DaemonMode.HotCold);

        host.Services.GetServices<IHostedService>().OfType<ProjectionCoordinator>().Any().ShouldBeTrue();
    }

    [Fact]
    public void add_async_daemon_on_ancillary_store_respects_externally_managed()
    {
        // Assert at the ServiceCollection level so nothing gets constructed
        var services = new ServiceCollection();
        services.AddMartenStore<IExternallyManagedAncillaryStore>(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DisableNpgsqlLogging = true;
        }).AddAsyncDaemon(DaemonMode.ExternallyManaged);

        services.Any(x =>
                x.ServiceType ==
                typeof(Marten.Events.Daemon.Coordination.IProjectionCoordinator<IExternallyManagedAncillaryStore>))
            .ShouldBeFalse();
    }

    [Fact]
    public void add_async_daemon_on_ancillary_store_with_solo_is_unaffected()
    {
        var services = new ServiceCollection();
        services.AddMartenStore<IExternallyManagedAncillaryStore>(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DisableNpgsqlLogging = true;
        }).AddAsyncDaemon(DaemonMode.Solo);

        services.Any(x =>
                x.ServiceType ==
                typeof(Marten.Events.Daemon.Coordination.IProjectionCoordinator<IExternallyManagedAncillaryStore>))
            .ShouldBeTrue();
    }

    private static string buildStoreWithAsyncProjectionCapturingConsole(DaemonMode mode)
    {
        // The "async daemon is disabled" warning is written straight to Console.Out from the
        // DocumentStore constructor, so swap the console writer around construction
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DisableNpgsqlLogging = true;
                opts.Projections.Snapshot<DaemonModeFlagView>(SnapshotLifecycle.Async);
                opts.Projections.AsyncMode = mode;
            });
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }
}

public interface IExternallyManagedAncillaryStore: IDocumentStore;

public record DaemonModeFlagRaised(Guid Id);

public record DaemonModeFlagView(Guid Id)
{
    public static DaemonModeFlagView Create(IEvent<DaemonModeFlagRaised> @event)
    {
        return new DaemonModeFlagView(@event.StreamId);
    }
}
