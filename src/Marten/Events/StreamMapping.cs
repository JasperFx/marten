using System;
using System.Collections.Generic;
using FubuCore;
using FubuCore.Util;
using Marten.Schema;

namespace Marten.Events
{
    public class StreamMapping : DocumentMapping
    {
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        public StreamMapping(Type aggregateType) : base(aggregateType)
        {
            if (!aggregateType.CanBeCastTo<IAggregate>())
                throw new ArgumentOutOfRangeException(nameof(aggregateType),
                    $"Only types implementing {typeof (IAggregate)} can be accepted");


            _events.OnMissing = type => { return new EventMapping(this, type); };

            StreamTypeName = aggregateType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

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