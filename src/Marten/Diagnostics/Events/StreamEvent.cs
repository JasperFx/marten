using Marten.Events;
using Microsoft.Extensions.Logging;

namespace Marten.Diagnostics.Events;

public abstract class StreamBaseDiagnosticEvent: DiagnosticEventBase
{
    public StreamBaseDiagnosticEvent(StreamAction streamAction, EventId eventId)
        : base(eventId)
    {
        StreamAction = streamAction;
    }

    public StreamAction StreamAction { get; }
}


