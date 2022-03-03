using System;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Marten failed to load events for a projection shard
    /// </summary>
    public class EventFetcherException: Exception
    {
        public EventFetcherException(ShardName name, IMartenDatabase martenDatabase, Exception innerException) : base($"Failure while trying to load events for projection shard '{name}@{martenDatabase.Identifier}'", innerException)
        {
        }
    }
}
