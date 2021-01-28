using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    public class SoloCoordinator: INodeCoordinator
    {
        public Task Start(IProjectionDaemon agent, CancellationToken token)
        {
            return agent.StartAll();
        }

        public Task Stop()
        {
            return Task.CompletedTask;
        }
    }
}
