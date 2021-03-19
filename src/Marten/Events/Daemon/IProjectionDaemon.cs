using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Starts, stops, and manages any running asynchronous projections
    /// </summary>
    public interface IProjectionDaemon : IDisposable
    {
        /// <summary>
        /// Rebuilds a single projection by projection name inline
        /// </summary>
        /// <param name="projectionName"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task RebuildProjection(string projectionName, CancellationToken token);


        /// <summary>
        /// Rebuilds a single projection by projection type inline
        /// </summary>
        /// <typeparam name="TView">Projection view type</typeparam>
        /// <param name="token"></param>
        /// <returns></returns>
        Task RebuildProjection<TView>(CancellationToken token);

        /// <summary>
        /// Starts a single projection shard by name
        /// </summary>
        /// <param name="shardName"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task StartShard(string shardName, CancellationToken token);

        /// <summary>
        /// Starts a single projection shard
        /// </summary>
        /// <param name="shard"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task StartShard(AsyncProjectionShard shard, CancellationToken token);

        /// <summary>
        /// Stops a single projection shard by name
        /// </summary>
        /// <param name="shardName"></param>
        /// <param name="ex"></param>
        /// <returns></returns>
        Task StopShard(string shardName, Exception ex = null);

        /// <summary>
        /// Starts all known projections shards
        /// </summary>
        /// <returns></returns>
        Task StartAll();

        /// <summary>
        /// Stops all known projection shards
        /// </summary>
        /// <returns></returns>
        Task StopAll();

        /// <summary>
        /// Observable tracking of projection shard events
        /// </summary>
        ShardStateTracker Tracker { get; }

        /// <summary>
        /// Starts the daemon high water detection. This is called
        /// automatically by any of the Start***() or Rebuild****()
        /// methods
        /// </summary>
        /// <returns></returns>
        Task StartDaemon();
    }
}
