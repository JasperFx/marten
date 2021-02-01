using System;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// A projection shard failed to start
    /// </summary>
    public class ShardStartException : Exception
    {
        public ShardStartException(ShardName name, Exception innerException) : base($"Failure while trying to stop '{name.Identity}'", innerException)
        {
        }
    }
}
