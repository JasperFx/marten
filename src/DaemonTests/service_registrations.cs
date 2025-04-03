using System.Linq;
using JasperFx.Events.Daemon;
using Lamar;
using Lamar.Microsoft.DependencyInjection;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace DaemonTests;

public class service_registrations
{
    [Fact]
    public void disabled_by_default()
    {
        new DaemonSettings().AsyncMode.ShouldBe(DaemonMode.Disabled);
    }

    [Fact]
    public void hosted_service_is_not_automatically_registered()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseLamar()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                });
            }).Build();

        var container = (IContainer)host.Services;


        container.Model
            .For<IHostedService>()
            .Instances
            .Any(x => x.ImplementationType == typeof(ProjectionCoordinator))
            .ShouldBeFalse();

        container.Model.For<IProjectionCoordinator>()
            .Instances.Any().ShouldBeFalse();
    }

    [Fact]
    public void when_registering_as_disabled()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseLamar()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                }).AddAsyncDaemon(DaemonMode.Disabled);
            }).Build();

        var container = (IContainer)host.Services;

        container.Model
            .For<IHostedService>()
            .Instances
            .Any(x => x.ImplementationType == typeof(ProjectionCoordinator))
            .ShouldBeFalse();

        container.Model.For<IProjectionCoordinator>()
            .Instances.Any().ShouldBeFalse();
    }

    [Fact]
    public void when_registering_as_solo()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseLamar()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                }).AddAsyncDaemon(DaemonMode.Solo);
            }).Build();

        var container = (IContainer)host.Services;

        container.GetAllInstances<IHostedService>().OfType<ProjectionCoordinator>()
            .Any().ShouldBeTrue();

        var coordinator = container.GetInstance<IProjectionCoordinator>()
            .ShouldBeOfType<ProjectionCoordinator>();

        coordinator.Distributor.ShouldBeOfType<SoloProjectionDistributor>();
    }

    [Fact]
    public void when_registering_as_HotCold_and_one_database()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseLamar()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                }).AddAsyncDaemon(DaemonMode.HotCold);
            }).Build();

        var container = (IContainer)host.Services;

        container.GetAllInstances<IHostedService>().OfType<ProjectionCoordinator>()
            .Any().ShouldBeTrue();

        var coordinator = container.GetInstance<IProjectionCoordinator>()
            .ShouldBeOfType<ProjectionCoordinator>();

        coordinator.Distributor.ShouldBeOfType<SingleTenantProjectionDistributor>();
    }

    [Fact]
    public void when_registering_as_HotCold_and_multiple_databases()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseLamar()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString);
                }).AddAsyncDaemon(DaemonMode.HotCold);
            }).Build();

        var container = (IContainer)host.Services;

        container.GetAllInstances<IHostedService>().OfType<ProjectionCoordinator>()
            .Any().ShouldBeTrue();

        var coordinator = container.GetInstance<IProjectionCoordinator>()
            .ShouldBeOfType<ProjectionCoordinator>();

        coordinator.Distributor.ShouldBeOfType<MultiTenantedProjectionDistributor>();
    }
}
