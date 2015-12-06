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

        private readonly Cache<Type, StreamMapping> _streams =
            new Cache<Type, StreamMapping>(type => new StreamMapping(type));

        public EventGraph()
        {
            _events.OnMissing = eventType =>
            {
                var stream = _streams.FirstOrDefault(x => x.HasEventType(eventType));

                return stream?.EventMappingFor(eventType);
            };

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };
        }

        public StreamMapping StreamMappingFor(Type aggregateType)
        {
            return _streams[aggregateType];
        }

        public StreamMapping StreamMappingFor<T>() where T : IAggregate
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
            return _streams.SelectMany(x => x.AllEvents());
        }

        public EventMapping EventMappingFor(string eventType)
        {
            return _byEventName[eventType];
        }
    }
}