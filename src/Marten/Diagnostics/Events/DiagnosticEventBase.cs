using Microsoft.Extensions.Logging;

namespace Marten.Diagnostics.Events;
#nullable enable
public class DiagnosticEventBase
{
    public DiagnosticEventBase(EventId eventId)
    {
        EventId = eventId;
        DisplayName = EventId.Name;
    }

    public EventId EventId { get; }
    public string? CorrelationId { get; set; }
    public virtual string? DisplayName { get; }
}
