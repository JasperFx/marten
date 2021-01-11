using System;

namespace Marten.Exceptions
{
    public class ProgressionProgressOutOfOrderException : Exception
    {
        public ProgressionProgressOutOfOrderException(string progressionOrShardName) : base($"Progression '{progressionOrShardName}' is out of order. This may happen when multiple processes try to process the projection")
        {
        }
    }
}
