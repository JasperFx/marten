using System.Linq;
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
        public void when_registering_as_disabled()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Events.Daemon.Mode = DaemonMode.Disabled;
                });
            });

            // No hosted service
            container.Model.For<IHostedService>().Instances.Any()
                .ShouldBeFalse();

            // No Node coordinator
            container.Model.For<INodeCoordinator>().Instances.Any()
                .ShouldBeFalse();

            // No projection daemon
            container.Model.For<IProjectionDaemon>().Instances.Any()
                .ShouldBeFalse();
        }

        [Fact]
        public void when_registering_as_solo()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Connection("fake-connection-string");
                    opts.Events.Daemon.Mode = DaemonMode.Solo;
                });
                x.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            });

            // Hosted service
            container.Model.For<IHostedService>().Instances
                .ShouldContain(x => x.ImplementationType == typeof(AsyncProjectionHostedService));

            // Node coordinator
            container.Model.For<INodeCoordinator>().Instances.Single()
                .ImplementationType.ShouldBe(typeof(SoloCoordinator));

            // Projection Daemon
            container.GetInstance<IProjectionDaemon>().ShouldBeOfType<ProjectionDaemon>();

            // Ensure resolution
            container.GetInstance<AsyncProjectionHostedService>().ShouldNotBeNull();
        }

        [Fact]
        public void when_registering_as_HotCold()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Connection("fake-connection-string");
                    opts.Events.Daemon.Mode = DaemonMode.HotCold;
                });
                x.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            });

            // hosted service
            container.Model.For<IHostedService>().Instances
                .ShouldContain(x => x.ImplementationType == typeof(AsyncProjectionHostedService));

            // Node coordinator
            container.Model.For<INodeCoordinator>().Instances.Single()
                .ImplementationType.ShouldBe(typeof(HotColdCoordinator));

            // Projection Daemon
            container.GetInstance<IProjectionDaemon>().ShouldBeOfType<ProjectionDaemon>();

            // Ensure resolution
            container.GetInstance<AsyncProjectionHostedService>().ShouldNotBeNull();
        }

    }
}
