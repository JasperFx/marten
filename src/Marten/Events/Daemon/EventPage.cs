using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.Daemon;

public class EventPage: List<IEvent>
{
    public EventPage(long floor)
    {
        Floor = floor;
    }

    public long Floor { get; }
    public long Ceiling { get; private set; }

    public void CalculateCeiling(int batchSize, long highWaterMark)
    {
        Ceiling = Count == batchSize
            ? this.Last().Sequence
            : highWaterMark;
    }
}