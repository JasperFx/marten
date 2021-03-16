using System;
using Marten.Events;

namespace Marten.Exceptions
{
    public class ApplyEventException : Exception
    {
        public ApplyEventException(IEvent @event, Exception innerException) : base($"Failure to apply event #{@event.Sequence} ({@event.Data}.)", innerException)
        {
            Event = @event;
        }

        public IEvent Event { get; }
    }
}
