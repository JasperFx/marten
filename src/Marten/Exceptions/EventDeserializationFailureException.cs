using System;
using LamarCodeGeneration;
using Marten.Events.Daemon;

namespace Marten.Exceptions
{
    /// <summary>
    /// Thrown if Marten encounters an exception while trying to deserialize
    /// or upcast a persisted event
    /// </summary>
    public class EventDeserializationFailureException : MartenException
    {
        public EventDeserializationFailureException(long sequence, Exception innerException) : base("Event deserialization error on sequence = " + sequence, innerException)
        {
            Sequence = sequence;
        }

        public long Sequence { get; }

        internal DeadLetterEvent ToDeadLetterEvent(ShardName name)
        {
            return new DeadLetterEvent
            {
                EventSequence = Sequence,
                ExceptionMessage = Message,
                ExceptionType = GetType().FullNameInCode(),
                ProjectionName = name.ProjectionName,
                ShardName = name.Key,
            };
        }
    }
}
