using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Coordinate the async daemon in the case of hot/cold failover
    /// where only one node at a time should be running the async daemon
    /// </summary>
    internal class HotColdCoordinator: INodeCoordinator
    {
        public Task Start(IProjectionDaemon daemon, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task Stop()
        {
            throw new NotImplementedException();
        }
    }
}
