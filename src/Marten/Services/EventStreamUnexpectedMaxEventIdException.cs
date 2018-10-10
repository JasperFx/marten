using System;

namespace Marten.Services
{
    public class EventStreamUnexpectedMaxEventIdException : Exception
    {
        public object Id { get; }

        public Type AggregateType { get; }

        public EventStreamUnexpectedMaxEventIdException(object id, Type aggregateType, int expected, int actual) : base($"Unexpected MAX(id) for event stream, expected {expected} but got {actual}")
        {
            Id = id;
            AggregateType = aggregateType;
        }
    }
}