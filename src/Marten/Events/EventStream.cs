using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

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

        private readonly IList<Event> _events = new List<Event>();

        public EventStream(Guid stream, Event[] events)
        {
            Id = stream;
            AddEvents(events);
        }

        public void AddEvents(IEnumerable<Event> events)
        {
            _events.AddRange(events);
            _events.Where(x => x.Id == Guid.Empty).Each(x => x.Id = Guid.NewGuid());
        }

        public IEnumerable<Event> Events => _events;
    }
}