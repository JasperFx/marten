using Marten.Events;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Marten.Diagnostics.Events;

public class StreamChangesFailedDiagnosticEvent: StreamBaseDiagnosticEvent
{
    public StreamChangesFailedDiagnosticEvent(StreamAction streamAction, Exception exception)
        : base(streamAction, DiagnosticEventId.StreamChangesFailed)
    {
        Exception = exception;
        DisplayName = $"Stream [{StreamAction.AggregateType.Name}] Changes Failed";
    }

    public Exception Exception { get; }
    public override string DisplayName { get; }
}


