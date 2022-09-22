using Marten.Diagnostics.Events;

namespace Marten.Diagnostics;
public interface IDiagnosticSource<TCategory>
    where TCategory : DiagnosticCategory<TCategory>, new()
{
    void Write(DiagnosticEventBase eventData);
}
