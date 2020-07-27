using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class UnknownEventTypeException: Exception
    {
        public UnknownEventTypeException(string eventTypeName) : base((string)$"Unknown event type name alias '{eventTypeName}.' You may need to register this event type through StoreOptions.Events.AddEventType(type)")
        {
        }

        protected UnknownEventTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
