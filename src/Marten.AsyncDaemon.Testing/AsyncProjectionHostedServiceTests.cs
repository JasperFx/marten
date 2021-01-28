using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class AsyncProjectionHostedServiceTests
    {
        private IProjectionDaemon theAgent = Substitute.For<IProjectionDaemon>();
        private INodeCoordinator theCoordinator = Substitute.For<INodeCoordinator>();
        private AsyncProjectionHostedService theHostedService;

        public AsyncProjectionHostedServiceTests()
        {
            theHostedService = new AsyncProjectionHostedService(theAgent, theCoordinator, Substitute.For<ILogger<AsyncProjectionHostedService>>());
        }

        [Fact]
        public async Task shutting_down_the_service()
        {
            var token = default(CancellationToken);
            await theHostedService.StopAsync(token);

            await theAgent.Received().StopAll();
            await theCoordinator.Received().Stop();
        }

        [Fact]
        public async Task starting_the_service()
        {
            var token = default(CancellationToken);
            await theHostedService.StartAsync(token);

            await theCoordinator.Received().Start(theAgent, token);
        }
    }
}
