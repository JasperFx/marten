using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten.Events
{
    public class EventStream
    {
        public static IEvent ToEvent(object @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            return typeof(Event<>).CloseAndBuildAs<IEvent>(@event, @event.GetType());
        }

        public EventStream(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        public Type AggregateType { get; set; } 

        private readonly IList<IEvent> _events = new List<IEvent>();

        public EventStream(Guid stream, IEvent[] events)
        {
            Id = stream;
            AddEvents(events);
        }

        public EventStream AddEvents(IEnumerable<IEvent> events)
        {
            _events.AddRange(events);
            _events.Where(x => x.Id == Guid.Empty).Each(x => x.Id = Guid.NewGuid());

            return this;
        }

        public IEnumerable<IEvent> Events => _events;

        /// <summary>
        /// Strictly for testing
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="@event"></param>
        /// <returns></returns>
        public EventStream Add<T>(T @event)
        {
            _events.Add(new Event<T>(@event) {Id = Guid.NewGuid()});
            return this;
        }
    }
}