using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.HighWater;
using Marten.Services;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Swappable coordinator for the async daemon that is
    /// responsible for starting projection shards and assigning
    /// work to the locally running IProjectionDaemon
    /// </summary>
    public interface INodeCoordinator : IDisposable
    {
        /// <summary>
        /// Called at the start of the application to register the projection
        /// daemon with the coordinator
        /// </summary>
        /// <param name="daemon"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task Start(IProjectionDaemon daemon, CancellationToken token);

        /// <summary>
        /// Called at application shutdown as a hook to perform
        /// any work necessary to take the current node down
        /// </summary>
        /// <returns></returns>
        Task Stop();
    }

}
