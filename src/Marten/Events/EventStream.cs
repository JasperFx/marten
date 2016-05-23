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

        public EventStream(Guid id, bool isNew)
        {
            Id = id;
            IsNew = isNew;
        }

        public Guid Id { get; }

        public bool IsNew { get; }

        public Type AggregateType { get; set; } 

        private readonly IList<IEvent> _events = new List<IEvent>();

        public EventStream(Guid stream, IEvent[] events, bool isNew)
        {
            Id = stream;
            AddEvents(events);
            IsNew = isNew;
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

        public void ApplyLatestVersion(int version)
        {
            var current = version;
            _events.Reverse().Each(e =>
            {
                e.Version = current;
                current--;
            });
        }
    }
}