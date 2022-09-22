using Marten.Diagnostics.Events;
using System.Diagnostics;

namespace Marten.Diagnostics;

internal static class Listener
{
    public static readonly DiagnosticSource Instance = new DiagnosticListener("Marten");
}

public class DiagnosticSource<TCategory>: IDiagnosticSource<TCategory>
    where TCategory : DiagnosticCategory<TCategory>, new()
{
    public void Write(DiagnosticEventBase eventData)
    {
        if (Listener.Instance.IsEnabled(eventData.EventId.Name))
        {
            Listener.Instance.Write(eventData.EventId.Name, eventData);
        }
    }
}
