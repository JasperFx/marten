using Microsoft.Extensions.Logging;

namespace Marten.Diagnostics;
public static class DiagnosticEventId
{
    private const int StreamBaseId = 10000;
    private const int ProjectionBaseId = 30000;
    private enum Id
    {
        //Stream
        StreamCreated = StreamBaseId,
        StreamAppended,
        StreamChangesCompleted,
        StreamChangesFailed,
    }

    private static EventId MakeStreamId(Id id)
        => new((int)id, DiagnosticCategory.Stream.Name + "." + id);
    private static EventId MakeProjectionId(Id id)
        => new((int)id, DiagnosticCategory.Projection.Name + "." + id);

    public static readonly EventId StreamCreated = MakeStreamId(Id.StreamCreated);
    public static readonly EventId StreamAppended = MakeStreamId(Id.StreamAppended);
    public static readonly EventId StreamChangesCompleted = MakeStreamId(Id.StreamChangesCompleted);
    public static readonly EventId StreamChangesFailed = MakeStreamId(Id.StreamChangesFailed);
}
