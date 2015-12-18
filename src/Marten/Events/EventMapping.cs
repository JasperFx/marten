using System;
using Baseline;
using Marten.Schema;

namespace Marten.Events
{
    public class EventMapping : DocumentMapping
    {
        public static string ToEventTypeName(Type eventType)
        {
            return eventType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public EventMapping(StreamMapping parent, Type eventType) : base(eventType)
        {
            if (!eventType.CanBeCastTo<IEvent>())
                throw new ArgumentOutOfRangeException(nameof(eventType),
                    $"Only types implementing {typeof (IEvent)} can be accepted");

            Stream = parent;

            EventTypeName = ToEventTypeName(eventType);
        }

        public string EventTypeName { get; set; }

        public StreamMapping Stream { get; }
    }
}