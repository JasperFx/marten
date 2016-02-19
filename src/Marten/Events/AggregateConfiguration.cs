using System;
using System.Collections.Generic;
using Baseline;
using Marten.Schema;

namespace Marten.Events
{
    public class AggregateConfiguration 
    {
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        public AggregateConfiguration(Type aggregateType)
        {
            if (!aggregateType.CanBeCastTo<IAggregate>())
                throw new ArgumentOutOfRangeException(nameof(aggregateType),
                    $"Only types implementing {typeof (IAggregate)} can be accepted");

            DocumentType = aggregateType;

            _events.OnMissing = type => new EventMapping(this, type);

            StreamTypeName = aggregateType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public Type DocumentType { get;}

        public string StreamTypeName { get; set; }

        public EventMapping AddEvent(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }


        public bool HasEventType(Type eventType)
        {
            return _events.Has(eventType);
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _events;
        }
    }
}