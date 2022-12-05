
using Marten.OpenTelemetry;
using Marten.OpenTelemetry.Internal;

namespace OpenTelemetry.Trace;
public static class TraceProviderBuilderExtensions
{
    public static TracerProviderBuilder AddMartenInstrumentation(this TracerProviderBuilder builder, Action<MartenInstrumentationOptions>? configure = null)
    {
        MartenInstrumentationOptions options = new();
        configure?.Invoke(options);
        builder.AddSource(MartenListener.SourceName);
        builder.AddInstrumentation(() => new OpenTelemetryInstrumentation(options));
        return builder;
    }
}
