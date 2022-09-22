using Marten.Events;

namespace Marten.Diagnostics.Events;

public class StreamChangesCompletedDiagnosticEvent: StreamBaseDiagnosticEvent
{
    public StreamChangesCompletedDiagnosticEvent(StreamAction streamAction)
        : base(streamAction, DiagnosticEventId.StreamChangesCompleted)
    {
        DisplayName = $"Stream [{StreamAction.AggregateType.Name}] Changes Completed";
    }

    public override string DisplayName { get; }
}


