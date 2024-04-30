#nullable enable
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Marten.Services;

public enum TrackLevel
{
    /// <summary>
    /// No Open Telemetry tracking
    /// </summary>
    None,

    /// <summary>
    /// Normal level of Open Telemetry tracking
    /// </summary>
    Normal,

    /// <summary>
    /// Very verbose event tracking, only suitable for debugging or performance tuning
    /// </summary>
    Verbose
}

public sealed class OpenTelemetryOptions
{
    /// <summary>
    /// Used to track OpenTelemetry events for opening an connection or exceptions on a connection, for example when a command or data reader has been executed. This defaults to false.
    /// </summary>
    public TrackLevel TrackConnections { get; set; } = TrackLevel.None;

    /// <summary>
    /// If already tracking connections, this will also tag the activities with any events appended with the connection
    /// </summary>
    public bool TrackEvents { get; set; }

    public bool ExportEventsAppended { get; set; }
    public bool ExportDocumentsStored { get; set; }
    public bool ExportDocumentsInserted { get; set; }
    public bool ExportDocumentsUpdated { get; set; }
    public bool ExportDocumentsChanged { get; set; }


    public Meter Meter { get; } = new Meter("Marten");

}
