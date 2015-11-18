using System;
using FubuCore;
using Marten.Schema;

namespace Marten.Events
{
    public class EventMapping : DocumentMapping
    {
        public EventMapping(StreamMapping parent, Type eventType) : base(eventType)
        {
            if (!eventType.CanBeCastTo<IEvent>())
                throw new ArgumentOutOfRangeException(nameof(eventType),
                    $"Only types implementing {typeof (IEvent)} can be accepted");

            Stream = parent;

            EventTypeName = eventType.Name.SplitPascalCase().ToLower().Replace(" ", "-");
        }

        public string EventTypeName { get; set; }

        public StreamMapping Stream { get; }
    }
}