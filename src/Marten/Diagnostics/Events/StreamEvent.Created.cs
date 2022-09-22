using Marten.Events;

namespace Marten.Diagnostics.Events;

public class StreamCreatedDiagnosticEvent: StreamBaseDiagnosticEvent
{
    public StreamCreatedDiagnosticEvent(StreamAction streamAction)
        : base(streamAction, DiagnosticEventId.StreamCreated)
    {
        DisplayName = $"Create Stream [{StreamAction.AggregateType.Name}]";
    }

    public override string DisplayName { get; }
}
