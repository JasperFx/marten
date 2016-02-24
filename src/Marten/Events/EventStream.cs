using System;
using System.Collections.Generic;

namespace Marten.Events
{
    public class EventStream
    {
        public EventStream(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        public Type AggregateType { get; set; } 

        public readonly IList<IEvent> Events = new List<IEvent>();
    }
}