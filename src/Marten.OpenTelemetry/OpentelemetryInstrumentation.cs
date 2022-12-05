using Marten.Diagnostics;
using Marten.OpenTelemetry.Internal;

namespace Marten.OpenTelemetry;
internal class OpenTelemetryInstrumentation: IDisposable
{
    private readonly DiagnosticSourceSubscriber subscriber;
    public OpenTelemetryInstrumentation(MartenInstrumentationOptions options)
    {
        options ??= new();
        MartenListener listener = new();
        subscriber = new DiagnosticSourceSubscriber(
            name => listener,
            listener => listener.Name == DiagnosticCategory.Name,
            null);

        subscriber.Subscribe();
    }
    public void Dispose()
    {
        subscriber.Dispose();
    }
}
