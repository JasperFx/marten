using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon
{
    public Task RebuildProjection(string projectionName, CancellationToken token)
    {
        return RebuildProjectionAsync(projectionName, token);
    }

    public Task RebuildProjection<TView>(CancellationToken token)
    {
        return RebuildProjectionAsync<TView>(token);
    }

    public Task RebuildProjection(Type projectionType, CancellationToken token)
    {
        return RebuildProjectionAsync(projectionType, token);
    }

    public Task RebuildProjection(Type projectionType, TimeSpan shardTimeout, CancellationToken token)
    {
        return RebuildProjectionAsync(projectionType, shardTimeout, token);
    }

    public Task RebuildProjection(string projectionName, TimeSpan shardTimeout, CancellationToken token)
    {
        return RebuildProjectionAsync(projectionName, shardTimeout, token);
    }

    public Task RebuildProjection<TView>(TimeSpan shardTimeout, CancellationToken token)
    {
        return RebuildProjectionAsync<TView>(shardTimeout, token);
    }

    public Task StartShard(string shardName, CancellationToken token)
    {
        return StartAgentAsync(shardName, token);
    }

    public Task StopShard(string shardName, Exception ex = null)
    {
        return StopAgentAsync(shardName, ex);
    }

    public Task StartAllShards()
    {
        return StartAllAsync();
    }

    public Task StopAll()
    {
        return StopAllAsync();
    }

    public Task StartDaemon()
    {
        return StartHighWaterDetectionAsync();
    }

    public Task PauseHighWaterAgent()
    {
        return PauseHighWaterAgentAsync();
    }
}
