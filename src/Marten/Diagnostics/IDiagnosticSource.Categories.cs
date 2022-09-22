namespace Marten.Diagnostics;
public class StreamDiagnosticSource: DiagnosticSource<DiagnosticCategory.Stream>
{
    public static readonly StreamDiagnosticSource Instance = new();
}
