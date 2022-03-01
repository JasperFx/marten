using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Default projection coordinator, assumes that there is only one
    /// single node
    /// </summary>
    internal class SoloCoordinator: INodeCoordinator
    {
        public IProjectionDaemon Daemon { get; private set; }

        public Task Start(IProjectionDaemon agent, CancellationToken token)
        {
            Daemon = agent;
            return agent.StartAllShards();
        }

        public Task Stop()
        {
            return Daemon.StopAll();
        }

        public void Dispose()
        {
            // Nothing
        }
    }
}
