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
        private IProjectionDaemon _agent;

        public Task Start(IProjectionDaemon agent, CancellationToken token)
        {
            _agent = agent;
            return agent.StartAllShards();
        }

        public Task Stop()
        {
            return _agent.StopAll();
        }
    }
}
