using System;
using JasperFx.Events.Projections;

namespace Marten.Events.Daemon.Internals;

internal class Command
{
    internal long HighWaterMark;
    internal long LastCommitted;
    internal EventRange Range;

    internal CommandType Type;

    internal static Command Completed(long ceiling)
    {
        return new Command { LastCommitted = ceiling, Type = CommandType.RangeCompleted };
    }

    internal static Command HighWaterMarkUpdated(long sequence)
    {
        return new Command { HighWaterMark = sequence, Type = CommandType.HighWater };
    }

    internal static Command Started(long highWater, long lastCommitted)
    {
        return new Command { HighWaterMark = highWater, LastCommitted = lastCommitted };
    }
}
