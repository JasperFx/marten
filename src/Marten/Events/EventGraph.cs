using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten.Events
{
    public class EventGraph
    {
        private readonly Cache<string, EventMapping> _byEventName = new Cache<string, EventMapping>();
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        private readonly Cache<Type, IAggregateStorage> _aggregates =
            new Cache<Type, IAggregateStorage>(type =>
            {
                return typeof (AggregateStorage<>).CloseAndBuildAs<IAggregateStorage>(type);
            });

        public EventGraph()
        {
            _events.OnMissing = eventType =>
            {
                var stream = _aggregates.FirstOrDefault(x => x.HasEventType(eventType));

                return stream?.EventMappingFor(eventType);
            };

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };
        }

        public IAggregateStorage StreamMappingFor(Type aggregateType)
        {
            return _aggregates[aggregateType];
        }

        public IAggregateStorage StreamMappingFor<T>() where T : IAggregate
        {
            return StreamMappingFor(typeof (T));
        }

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor<T>() where T : IEvent
        {
            return EventMappingFor(typeof (T));
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _aggregates.SelectMany(x => x.AllEvents());
        }

        public EventMapping EventMappingFor(string eventType)
        {
            return _byEventName[eventType];
        }
    }
}