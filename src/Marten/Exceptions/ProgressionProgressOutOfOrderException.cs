using System;
using Marten.Events.Daemon;

namespace Marten.Exceptions
{
    public class ProgressionProgressOutOfOrderException : Exception
    {
        public ProgressionProgressOutOfOrderException(ShardName progressionOrShardName) : base($"Progression '{progressionOrShardName}' is out of order. This may happen when multiple processes try to process the projection")
        {
        }
    }
}
