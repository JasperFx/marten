using System;

namespace Marten.Events.Daemon
{
    public class ShardStopException : Exception
    {
        public ShardStopException(ShardName name, Exception innerException) : base($"Failure while trying to stop '{name.Identity}'", innerException)
        {
        }
    }

    public class ShardStartException : Exception
    {
        public ShardStartException(ShardName name, Exception innerException) : base($"Failure while trying to stop '{name.Identity}'", innerException)
        {
        }
    }

    public class EventFetcherException: Exception
    {
        public EventFetcherException(ShardName name, Exception innerException) : base($"Failure while trying to load events for projection shard '{name}'", innerException)
        {
        }
    }
}
