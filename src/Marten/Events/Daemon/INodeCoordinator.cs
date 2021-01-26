using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    public interface INodeCoordinator
    {
        Task Start(INodeAgent agent, CancellationToken token);
        Task Stop();
    }

    public class HotColdCoordinator: INodeCoordinator
    {
        public Task Start(INodeAgent agent, CancellationToken token)
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
        public Task Start(INodeAgent agent, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task Stop()
        {
            throw new NotImplementedException();
        }
    }



    public interface INodeAgent
    {
        Task StartShard(string shardName);
        Task StartShard(IAsyncProjectionShard shard);
        Task StopShard(string shardName);

        Task StartAll();
        Task StopAll();

        ShardStateTracker Tracker { get; }
    }

    // This will be used by a single
    public interface IDaemon : INodeAgent
    {
        Task RebuildProjection(string projectionName);
        Task RebuildProjection(Type viewType);
    }
}
