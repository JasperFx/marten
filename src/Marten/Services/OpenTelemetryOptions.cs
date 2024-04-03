#nullable enable
namespace Marten.Services;

public sealed class OpenTelemetryOptions
{
    /// <summary>
    /// Used to track OpenTelemetry events for a connection, for example when a command or data reader has been executed. This defaults to false.
    /// </summary>
    public bool TrackConnectionEvents { get; set; }
}
