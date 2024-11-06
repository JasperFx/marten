using System;
using JasperFx.Events;
using Marten.Events;

namespace Marten.Exceptions;

public class ApplyEventException: MartenException
{
    public ApplyEventException(IEvent @event, Exception innerException): base(
        $"Failure to apply event #{@event.Sequence} Id({@event.Id})", innerException)
    {
        Event = @event;
    }

    public IEvent Event { get; }
}
