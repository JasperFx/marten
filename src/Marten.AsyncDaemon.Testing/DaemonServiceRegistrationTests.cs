using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lamar;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class DaemonServiceRegistrationTests
    {
        [Fact]
        public void disabled_by_default()
        {
            new DaemonSettings().AsyncMode.ShouldBe(DaemonMode.Disabled);
        }

        [Fact]
        public async Task hosted_service_is_not_automatically_registered()
        {
            var logger = Substitute.For<ILogger<AsyncProjectionHostedService>>();
            await using var container = new Container(x =>
            {
                x.For(typeof(ILogger<>)).Use(typeof(NullLogger<>));
                x.AddSingleton(typeof(ILogger<AsyncProjectionHostedService>), logger);
                x.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                });

                //.AddAsyncDaemon(DaemonMode.Disabled);
            });

            container.Model.For<IHostedService>().Instances.Any().ShouldBeFalse();


        }

        [Fact]
        public async Task when_registering_as_disabled()
        {
            var logger = Substitute.For<ILogger<AsyncProjectionHostedService>>();
            await using var container = new Container(x =>
            {
                x.For(typeof(ILogger<>)).Use(typeof(NullLogger<>));
                x.AddSingleton(typeof(ILogger<AsyncProjectionHostedService>), logger);
                x.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                }).AddAsyncDaemon(DaemonMode.Disabled);
            });

            var service = container
                .GetAllInstances<IHostedService>()
                .OfType<AsyncProjectionHostedService>()
                .Single();

            await service.StartAsync(CancellationToken.None);

            service.Coordinators.Any().ShouldBeFalse();

            await service.StopAsync(CancellationToken.None);

            logger.DidNotReceive().LogDebug("Stopping the asynchronous projection agent");
        }

        [Fact]
        public async Task when_registering_as_solo()
        {
            var logger = Substitute.For<ILogger<AsyncProjectionHostedService>>();
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Projections.AsyncMode = DaemonMode.Solo;
                }).AddAsyncDaemon(DaemonMode.Solo);
                x.For(typeof(ILogger<>)).Use(typeof(NullLogger<>));
                x.AddSingleton(typeof(ILogger<AsyncProjectionHostedService>), logger);
            });

            var service = container
                .GetAllInstances<IHostedService>()
                .OfType<AsyncProjectionHostedService>()
                .Single();
            await service.StartAsync(CancellationToken.None);

            service.Coordinators.Any().ShouldBeTrue();
            service.Coordinators.All(x => x is SoloCoordinator).ShouldBeTrue();

            await service.StopAsync(CancellationToken.None);

            logger.Received().LogDebug("Stopping the asynchronous projection agent");
        }

        [Fact]
        public async Task when_registering_as_HotCold()
        {
            var logger = Substitute.For<ILogger<AsyncProjectionHostedService>>();
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Projections.AsyncMode = DaemonMode.HotCold;
                }).AddAsyncDaemon(DaemonMode.HotCold);
                x.For(typeof(ILogger<>)).Use(typeof(NullLogger<>));
                x.AddSingleton(typeof(ILogger<AsyncProjectionHostedService>), logger);
            });

            var service = container
                .GetAllInstances<IHostedService>()
                .OfType<AsyncProjectionHostedService>()
                .Single();
            await service.StartAsync(CancellationToken.None);

            service.Coordinators.Any().ShouldBeTrue();
            service.Coordinators.All(x => x is HotColdCoordinator).ShouldBeTrue();

            await service.StopAsync(CancellationToken.None);

            logger.Received().LogDebug("Stopping the asynchronous projection agent");
        }

    }
}
