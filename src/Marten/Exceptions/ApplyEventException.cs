using System;
using JasperFx.Events;
using Marten.Events;

namespace Marten.Exceptions;

// #5337: dead compat type. Since the async daemon moved to JasperFx.Events, apply
// failures throw JasperFx.Events.Daemon.ApplyEventException — nothing in Marten
// throws this type any longer. It stays (as [Obsolete]) so existing catch blocks
// keep compiling through 9.x; catch the JasperFx.Events.Daemon type instead.
[Obsolete(
    "Marten no longer throws this type. Catch JasperFx.Events.Daemon.ApplyEventException instead. This duplicate will be removed in Marten 10.")]
public class ApplyEventException: MartenException
{
    public ApplyEventException(IEvent @event, Exception innerException): base(
        $"Failure to apply event #{@event.Sequence} Id({@event.Id})", innerException)
    {
        Event = @event;
    }

    public IEvent Event { get; }
}
