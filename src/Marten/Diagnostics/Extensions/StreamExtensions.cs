using Marten.Diagnostics.Events;
using Marten.Events;
using System;

#nullable enable

namespace Marten.Diagnostics.Extensions;
public static class EventStoreDiagnosticExtensions
{
    public static void StartStream(this IDiagnosticSource<DiagnosticCategory.Stream> diagnostic, StreamAction streamAction, string? correlationId)
    {
        StreamCreatedDiagnosticEvent @event = new(streamAction)
        {
            CorrelationId = correlationId
        };
        diagnostic.Write(@event);
    }

    public static void AppendStream(this IDiagnosticSource<DiagnosticCategory.Stream> diagnostic, StreamAction streamAction, string? correlationId)
    {
        StreamAppendedDiagnosticEvent @event = new(streamAction)
        {
            CorrelationId = correlationId
        };
        diagnostic.Write(@event);
    }

    public static void SaveStreamChanges(this IDiagnosticSource<DiagnosticCategory.Stream> diagnostic, StreamAction streamAction, string? correlationId)
    {
        StreamChangesCompletedDiagnosticEvent @event = new(streamAction)
        {
            CorrelationId = correlationId
        };
        diagnostic.Write(@event);
    }

    public static void FailStreamChanges(this IDiagnosticSource<DiagnosticCategory.Stream> diagnostic, StreamAction streamAction, string? correlationId, Exception exception)
    {
        StreamChangesFailedDiagnosticEvent @event = new(streamAction, exception)
        {
            CorrelationId = correlationId
        };
        diagnostic.Write(@event);
    }
}
