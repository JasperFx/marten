using JasperFx.Events.Projections;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Operations;

namespace Marten.Events.Daemon.Internals;

internal static class EventRangeExtensions
{
    internal static IStorageOperation BuildProgressionOperation(this EventRange range, EventGraph events)
    {
        if (range.SequenceFloor == 0)
        {
            return new InsertProjectionProgress(events, range);
        }

        return new UpdateProjectionProgress(events, range);
    }
}
