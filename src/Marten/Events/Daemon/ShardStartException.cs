using System;
using Marten.Exceptions;

namespace Marten.Events.Daemon;

/// <summary>
///     A projection shard failed to start
/// </summary>
public class ShardStartException: MartenException
{
    internal ShardStartException(ShardAgent agent, Exception innerException): base(
        $"Failure while trying to stop '{agent.ProjectionShardIdentity}'", innerException)
    {
    }

    internal ShardStartException(AsyncProjectionShard shard, Exception innerException): base(
        $"Failure while trying to stop '{shard.Name.Identity}'", innerException)
    {
    }
}
