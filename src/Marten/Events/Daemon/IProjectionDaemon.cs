#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;

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
    ///     Indicates if this daemon is currently running any subscriptions
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     Rebuilds a single projection by projection name inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <param name="projectionName"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjectionAsync(string projectionName, CancellationToken token);


    /// <summary>
    ///     Rebuilds a single projection by projection type inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <typeparam name="TView">Projection view type</typeparam>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjectionAsync<TView>(CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection type inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <param name="projectionType">The projection type</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjectionAsync(Type projectionType, CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection name inline
    /// </summary>
    /// <param name="projectionType">The projection type</param>
    /// <param name="shardTimeout"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjectionAsync(Type projectionType, TimeSpan shardTimeout, CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection name inline
    /// </summary>
    /// <param name="projectionName"></param>
    /// <param name="shardTimeout"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjectionAsync(string projectionName, TimeSpan shardTimeout, CancellationToken token);


    /// <summary>
    ///     Rebuilds a single projection by projection type inline
    /// </summary>
    /// <typeparam name="TView">Projection view type</typeparam>
    /// <param name="token"></param>
    /// <returns></returns>
    Task RebuildProjectionAsync<TView>(TimeSpan shardTimeout, CancellationToken token);

    /// <summary>
    ///     Starts a single projection shard by name
    /// </summary>
    /// <param name="shardName">The full identity of the desired shard. Example 'Trip:All'</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task StartAgentAsync(string shardName, CancellationToken token);

    /// <summary>
    ///     Stops a single projection shard by name
    /// </summary>
    /// <param name="shardName">The full identity of the desired shard. Example 'Trip:All'</param>
    /// <param name="ex"></param>
    /// <returns></returns>
    Task StopAgentAsync(string shardName, Exception? ex = null);

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

    public long HighWaterMark();
    AgentStatus StatusFor(string shardName);

    /// <summary>
    ///     List of agents that are currently running or paused
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<ISubscriptionAgent> CurrentAgents();

    /// <summary>
    ///     Are there any paused agents?
    /// </summary>
    /// <returns></returns>
    bool HasAnyPaused();

    /// <summary>
    ///     Will eject a Paused
    /// </summary>
    /// <param name="shardName"></param>
    void EjectPausedShard(string shardName);

    /* START OLD STUFF *****************************************/

    /// <summary>
    ///     Rebuilds a single projection by projection name inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <param name="projectionName"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer RebuildProjectionAsync() overloads")]
    Task RebuildProjection(string projectionName, CancellationToken token);


    /// <summary>
    ///     Rebuilds a single projection by projection type inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <typeparam name="TView">Projection view type</typeparam>
    /// <param name="token"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer RebuildProjectionAsync() overloads")]
    Task RebuildProjection<TView>(CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection type inline.
    ///     Will timeout if a shard takes longer than 5 minutes.
    /// </summary>
    /// <param name="projectionType">The projection type</param>
    /// <param name="token"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer RebuildProjectionAsync() overloads")]
    Task RebuildProjection(Type projectionType, CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection name inline
    /// </summary>
    /// <param name="projectionType">The projection type</param>
    /// <param name="shardTimeout"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer RebuildProjectionAsync() overloads")]
    Task RebuildProjection(Type projectionType, TimeSpan shardTimeout, CancellationToken token);

    /// <summary>
    ///     Rebuilds a single projection by projection name inline
    /// </summary>
    /// <param name="projectionName"></param>
    /// <param name="shardTimeout"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer RebuildProjectionAsync() overloads")]
    Task RebuildProjection(string projectionName, TimeSpan shardTimeout, CancellationToken token);


    /// <summary>
    ///     Rebuilds a single projection by projection type inline
    /// </summary>
    /// <typeparam name="TView">Projection view type</typeparam>
    /// <param name="token"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer RebuildProjectionAsync() overloads")]
    Task RebuildProjection<TView>(TimeSpan shardTimeout, CancellationToken token);

    /// <summary>
    ///     Starts a single projection shard by name
    /// </summary>
    /// <param name="shardName"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer StartAgentAsync() instead")]
    Task StartShard(string shardName, CancellationToken token);

    /// <summary>
    ///     Stops a single projection shard by name
    /// </summary>
    /// <param name="shardName"></param>
    /// <param name="ex"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer StopAgentAsync() instead")]
    Task StopShard(string shardName, Exception? ex = null);

    /// <summary>
    ///     Starts all known projections shards
    /// </summary>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer StartAllAsync() instead")]
    Task StartAllShards();

    /// <summary>
    ///     Stops all known projection shards
    /// </summary>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer StopAllAsync() instead")]
    Task StopAll();

    /// <summary>
    ///     Starts the daemon high water detection. This is called
    ///     automatically by any of the Start***() or Rebuild****()
    ///     methods
    /// </summary>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Prefer StartHighWaterDetectionAsync() instead")]
    Task StartDaemon();


    [Obsolete("Will be removed in 8.0. Prefer PauseHighWaterAgentAsync()")]
    Task PauseHighWaterAgent();

    /// <summary>
    ///     Starts the underlying "high water agent" running if it is not already running. This is an advanced usage
    ///     that's handled automatically for the most part by Marten
    /// </summary>
    /// <returns></returns>
    Task StartHighWaterDetectionAsync();

    /// <summary>
    ///     Manually stop the "high water agent" inside of this daemon. This is an advanced usage
    ///     that's handled automatically for the most part by Marten
    /// </summary>
    /// <returns></returns>
    Task PauseHighWaterAgentAsync();

    /// <summary>
    ///     Wait until the named shard has been started
    /// </summary>
    /// <param name="shardName"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    Task WaitForShardToBeRunning(string shardName, TimeSpan timeout);


    /// <summary>
    ///     Rewinds a subscription (or projection, so be careful with this usage) to a certain point
    ///     and allows it to restart at that point
    /// </summary>
    /// <param name="subscriptionName">Name of the subscription</param>
    /// <param name="token"></param>
    /// <param name="sequenceFloor">The point at which to rewind the subscription. The default is zero</param>
    /// <param name="timestamp">
    ///     Optional parameter to rewind the subscription to rerun any events that were posted on or after
    ///     this time. If Marten cannot determine the sequence, it will do nothing
    /// </param>
    /// <returns></returns>
    Task RewindSubscriptionAsync(string subscriptionName, CancellationToken token, long? sequenceFloor = 0,
        DateTimeOffset? timestamp = null);
}
