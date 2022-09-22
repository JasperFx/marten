using Marten.Diagnostics;
using System.Diagnostics;

namespace Marten.OpenTelemetry.Internal;

internal partial class MartenListener: IObserver<KeyValuePair<string, object?>>
{
    private static readonly string version = typeof(MartenListener).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    public static string SourceName => "Marten.OpenTelemetry";
    private ActivitySource ActivitySource { get; } = new(SourceName, version);
    private Dictionary<string[], Action<object?>> handlers { get; } = new();

    public MartenListener()
    {
        handlers.Add(new[]
        {
            DiagnosticEventId.StreamCreated.Name!,
            DiagnosticEventId.StreamAppended.Name!
        }, StreamCreated);

        handlers.Add(new[]
        {
            DiagnosticEventId.StreamChangesCompleted.Name!,
            DiagnosticEventId.StreamChangesFailed.Name!
        }, StreamChangesFinished);
    }

    public virtual void OnNext(KeyValuePair<string, object?> evt)
    {
        var key = handlers.Keys.FirstOrDefault(key => key.Contains(evt.Key));
        if (key == null)
            return;

        handlers.TryGetValue(key, out var handler);
        handler?.Invoke(evt.Value);
    }

    public virtual void OnCompleted() { }
    public virtual void OnError(Exception error) { }
}
