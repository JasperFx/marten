using System.Linq;
using Lamar;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
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
        }

        [Fact]
        public void when_registering_as_solo()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Events.Daemon.Mode = DaemonMode.Solo;
                });
            });

            // No hosted service
            container.Model.For<IHostedService>().Instances
                .ShouldContain(x => x.ImplementationType == typeof(AsyncProjectionHostedService));

            // No Node coordinator
            container.Model.For<INodeCoordinator>().Instances.Single()
                .ImplementationType.ShouldBe(typeof(SoloCoordinator));
        }

        [Fact]
        public void when_registering_as_HotCold()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Events.Daemon.Mode = DaemonMode.HotCold;
                });
            });

            // No hosted service
            container.Model.For<IHostedService>().Instances
                .ShouldContain(x => x.ImplementationType == typeof(AsyncProjectionHostedService));

            // No Node coordinator
            container.Model.For<INodeCoordinator>().Instances.Single()
                .ImplementationType.ShouldBe(typeof(HotColdCoordinator));
        }

        [Fact]
        public void when_registering_as_Distributed()
        {
            var container = new Container(x =>
            {
                x.AddMarten(opts =>
                {
                    opts.Events.Daemon.Mode = DaemonMode.Distributed;
                });
            });

            // No hosted service
            container.Model.For<IHostedService>().Instances
                .ShouldContain(x => x.ImplementationType == typeof(AsyncProjectionHostedService));

            // No Node coordinator
            container.Model.For<INodeCoordinator>().Instances.Single()
                .ImplementationType.ShouldBe(typeof(DistributedCoordinator));
        }
    }
}
