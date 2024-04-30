#nullable enable
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Marten.Services;

public sealed class OpenTelemetryOptions
{
    /// <summary>
    /// Used to track OpenTelemetry events for a connection, for example when a command or data reader has been executed. This defaults to false.
    /// </summary>
    public bool TrackConnectionEvents { get; set; }

    /// <summary>
    /// Name of the metrics source name for Marten within this application. The default is "Marten"
    /// </summary>
    public string MetricsSourceName { get; set; } = "Marten";

    public bool ExportEventsAppended { get; set; }
    public bool ExportDocumentsStored { get; set; }
    public bool ExportDocumentsInserted { get; set; }
    public bool ExportDocumentsUpdated { get; set; }
    public bool ExportDocumentsChanged { get; set; }


    public Meter Meter { get; } = new Meter("Marten");

}
