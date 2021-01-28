using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    public interface INodeCoordinator
    {
        Task Start(IProjectionDaemon daemon, CancellationToken token);
        Task Stop();
    }

    public class HotColdCoordinator: INodeCoordinator
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

    public class DistributedCoordinator: INodeCoordinator
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


    // This will be used by a single
    public interface IProjectionDaemon : IDisposable
    {
        Task RebuildProjection(string projectionName, CancellationToken token);

        // TODO -- option to rebuild by projection type? view type?


        Task StartShard(string shardName, CancellationToken token);
        Task StartShard(IAsyncProjectionShard shard, CancellationToken token);
        Task StopShard(string shardName);

        Task StartAll();
        Task StopAll();

        ShardStateTracker Tracker { get; }
    }
}
