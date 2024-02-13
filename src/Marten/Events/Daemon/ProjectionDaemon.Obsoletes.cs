using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon
{
    public Task RebuildProjection(string projectionName, CancellationToken token) =>
        RebuildProjectionAsync(projectionName, token);

    public Task RebuildProjection<TView>(CancellationToken token) => RebuildProjectionAsync<TView>(token);

    public Task RebuildProjection(Type projectionType, CancellationToken token) =>
        RebuildProjectionAsync(projectionType, token);

    public Task RebuildProjection(Type projectionType, TimeSpan shardTimeout, CancellationToken token) =>
        RebuildProjectionAsync(projectionType, shardTimeout, token);

    public Task RebuildProjection(string projectionName, TimeSpan shardTimeout, CancellationToken token) =>
        RebuildProjectionAsync(projectionName, shardTimeout, token);

    public Task RebuildProjection<TView>(TimeSpan shardTimeout, CancellationToken token) =>
        RebuildProjectionAsync<TView>(shardTimeout, token);

    public Task StartShard(string shardName, CancellationToken token) => StartAgentAsync(shardName, token);

    public Task StopShard(string shardName, Exception ex = null) => StopAgentAsync(shardName, ex);

    public Task StartAllShards() => StartAllAsync();

    public Task StopAll() => StopAllAsync();

    public Task StartDaemon() => StartHighWaterDetectionAsync();

    public Task PauseHighWaterAgent() => PauseHighWaterAgentAsync();
}
