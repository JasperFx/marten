using System;
using Marten.Exceptions;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// A projection shard failed to stop in a timely manner
    /// </summary>
    public class ShardStopException : MartenException
    {
        public ShardStopException(ShardName name, Exception innerException) : base($"Failure while trying to stop '{name.Identity}'", innerException)
        {
        }
    }
}
