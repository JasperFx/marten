using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class EventStreamUnexpectedMaxEventIdException: ConcurrencyException
    {
        public Type AggregateType { get; }

        public EventStreamUnexpectedMaxEventIdException(object id, Type aggregateType, long expected, long actual) : base($"Unexpected starting version number for event stream '{id}', expected {expected} but was {actual}", aggregateType, id)
        {
            Id = id;
            AggregateType = aggregateType;
        }

        protected EventStreamUnexpectedMaxEventIdException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
