using System;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Progress;
using Weasel.Core.Operations;

namespace Marten.Events.Daemon.Internals;

[Obsolete("Try to put this behind a new JasperFx abstraction")]
public static class EventRangeExtensions
{
    // START HERE!!!
    internal static IStorageOperation BuildProgressionOperation(this EventRange range, EventGraph events)
    {
        if (range.SequenceFloor == 0)
        {
            return new InsertProjectionProgress(events, range);
        }

        return new UpdateProjectionProgress(events, range);
    }
}
