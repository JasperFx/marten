using System;

namespace Marten.Events
{
    public class UnknownEventTypeException : Exception
    {
        public UnknownEventTypeException(string eventTypeName) : base((string) $"Unknown event type name alias '{eventTypeName}.' You may need to register this event type through StoreOptions.Events.AddEventType(type)")
        {
        }
    }
}