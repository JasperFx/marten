#nullable enable
using System.Reflection;

namespace Marten.Services;

public sealed class OpenTelemetryOptions
{
    /// <summary>
    /// Used to track OpenTelemetry events for a connection, for example when a command or data reader has been executed. This defaults to false.
    /// </summary>
    public bool TrackConnectionEvents { get; set; }

    /// <summary>
    /// Name of the metrics source name for Marten within this application. The default is Marten:{entry assembly name}
    /// </summary>
    public string MetricsSourceName { get; set; } = $"Marten:{Assembly.GetEntryAssembly().GetName().Name}";


}
