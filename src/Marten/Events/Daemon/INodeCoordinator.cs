using System;
using System.Threading.Tasks;
using Marten.Events.Projections;

namespace Marten.Events.Daemon
{
    public interface INodeCoordinator
    {
        Task Manage(INodeAgent agent);

        Task Started(IAsyncProjectionShard shard);
        Task Paused(IAsyncProjectionShard shard);
        Task Stopped(IAsyncProjectionShard shard);

        Task FailedToStart(IAsyncProjectionShard shard);
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
