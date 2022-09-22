using Marten.Events;

namespace Marten.Diagnostics.Events;

public class StreamAppendedDiagnosticEvent: StreamBaseDiagnosticEvent
{
    public StreamAppendedDiagnosticEvent(StreamAction streamAction)
        : base(streamAction, DiagnosticEventId.StreamAppended)
    {
        DisplayName = $"Append Stream [{StreamAction.AggregateType.Name}]";
    }

    public override string DisplayName { get; }
}


