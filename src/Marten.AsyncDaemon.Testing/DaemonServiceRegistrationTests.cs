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
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class DaemonServiceRegistrationTests
    {
        [Fact]
        public void disabled_by_default()
        {
            new DaemonSettings().Mode.ShouldBe(DaemonMode.Disabled);
        }

        [Fact]
        public async Task when_registering_as_disabled()
        {
            using var container = new Container(x =>
            {
                x.For(typeof(ILogger<>)).Use(typeof(NullLogger<>));
                x.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Events.Daemon.Mode = DaemonMode.Disabled;
                });
            });

            var service = container.GetInstance<AsyncProjectionHostedService>();
            await service.StartAsync(CancellationToken.None);

            service.Agent.ShouldBeNull();

        }

        [Fact]
        public async Task when_registering_as_solo()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Events.Daemon.Mode = DaemonMode.Solo;
                });
                x.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            });

            var service = container.GetInstance<AsyncProjectionHostedService>();
            await service.StartAsync(CancellationToken.None);

            service.Agent.ShouldNotBeNull();
            service.Coordinator.ShouldBeOfType<SoloCoordinator>();

            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task when_registering_as_HotCold()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Events.Daemon.Mode = DaemonMode.HotCold;
                });
                x.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            });

            var service = container.GetInstance<AsyncProjectionHostedService>();
            await service.StartAsync(CancellationToken.None);

            service.Agent.ShouldNotBeNull();
            service.Coordinator.ShouldBeOfType<HotColdCoordinator>();

            await service.StopAsync(CancellationToken.None);
        }

    }
}
