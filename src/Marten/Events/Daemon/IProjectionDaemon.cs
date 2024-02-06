#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.New;

namespace Marten.Events.Daemon;

/// <summary>
///     Starts, stops, and manages any running asynchronous projections
/// </summary>
public interface IProjectionDaemon: IDisposable
{
    /// <summary>
    ///     Observable tracking of projection shard events
    /// </summary>
    ShardStateTracker Tracker { get; }

    /// <summary>
    /// Indicates if this daemon is currently running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     Rebuilds a single projection by projection name inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <param name="projectionName"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjection(string projectionName, CancellationToken token);


    /// <summary>
    ///     Rebuilds a single projection by projection type inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <typeparam name="TView">Projection view type</typeparam>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjection<TView>(CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection type inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <param name="projectionType">The projection type</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjection(Type projectionType, CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection name inline
    /// </summary>
    /// <param name="projectionType">The projection type</param>
    /// <param name="shardTimeout"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjection(Type projectionType, TimeSpan shardTimeout, CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection name inline
    /// </summary>
    /// <param name="projectionName"></param>
    /// <param name="shardTimeout"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjection(string projectionName, TimeSpan shardTimeout, CancellationToken token);


    /// <summary>
    ///     Rebuilds a single projection by projection type inline
    /// </summary>
    /// <typeparam name="TView">Projection view type</typeparam>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjection<TView>(TimeSpan shardTimeout, CancellationToken token);

    /// <summary>
    ///     Starts a single projection shard by name
    /// </summary>
    /// <param name="shardName"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task StartShard(string shardName, CancellationToken token);

    /// <summary>
    ///     Stops a single projection shard by name
    /// </summary>
    /// <param name="shardName"></param>
    /// <param name="ex"></param>
    /// <returns></returns>
    Task StopShard(string shardName, Exception? ex = null);

    /// <summary>
    ///     Starts all known projections shards
    /// </summary>
    /// <returns></returns>
    Task StartAllShards();

    /// <summary>
    ///     Stops all known projection shards
    /// </summary>
    /// <returns></returns>
    Task StopAllAsync();

    /// <summary>
    ///     Starts the daemon high water detection. This is called
    ///     automatically by any of the Start***() or Rebuild****()
    ///     methods
    /// </summary>
    /// <returns></returns>
    Task StartDaemonAsync();


    /// <summary>
    ///     Use with caution! This will try to wait for all projections to "catch up" to the currently
    ///     known farthest known sequence of the event store
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    Task WaitForNonStaleData(TimeSpan timeout);

    Task PauseHighWaterAgent();

    long HighWaterMark();
    AgentStatus StatusFor(string shardName);
    IReadOnlyList<ISubscriptionAgent> CurrentShards();
}
