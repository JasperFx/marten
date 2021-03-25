using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using NSubstitute;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class SoloCoordinatorTests
    {
        [Fact]
        public async Task start_starts_them_all()
        {
            var agent = Substitute.For<IProjectionDaemon>();
            var coordinator = new SoloCoordinator();
            await coordinator.Start(agent, CancellationToken.None);

            await agent.Received().StartAllShards();
        }
    }
}
