#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    Task StopAsync(string shardName, Exception? ex = null);

    /// <summary>
    ///     Starts all known projections shards
    /// </summary>
    /// <returns></returns>
    Task StartAllAsync();

    /// <summary>
    ///     Stops all known projection shards
    /// </summary>
    /// <returns></returns>
    Task StopAllAsync();

    /// <summary>
    ///     Use with caution! This will try to wait for all projections to "catch up" to the currently
    ///     known farthest known sequence of the event store
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    Task WaitForNonStaleData(TimeSpan timeout);

    long HighWaterMark();
    AgentStatus StatusFor(string shardName);

    /// <summary>
    /// List of agents that are currently running or paused
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<ISubscriptionAgent> CurrentAgents();

    /// <summary>
    /// Are there any paused agents?
    /// </summary>
    /// <returns></returns>
    bool HasAnyPaused();

    /// <summary>
    /// Will eject a Paused
    /// </summary>
    /// <param name="shardName"></param>
    void EjectPausedShard(string shardName);
}
