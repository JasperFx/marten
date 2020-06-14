using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class EventStreamUnexpectedMaxEventIdException: Exception
    {
        public object Id { get; }

        public Type AggregateType { get; }

        public EventStreamUnexpectedMaxEventIdException(object id, Type aggregateType, int expected, int actual) : base($"Unexpected MAX(id) for event stream, expected {expected} but got {actual}")
        {
            Id = id;
            AggregateType = aggregateType;
        }

        protected EventStreamUnexpectedMaxEventIdException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
