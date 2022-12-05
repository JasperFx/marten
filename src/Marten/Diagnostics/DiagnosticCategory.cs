namespace Marten.Diagnostics;
public static class DiagnosticCategory
{
    public const string Name = "Marten";

    public class Stream: DiagnosticCategory<Stream> { }
    public class Projection: DiagnosticCategory<Projection> { }
}
