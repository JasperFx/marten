using System;

namespace Marten.Events.Daemon.Internals;

internal class Command
{
    internal long HighWaterMark;
    internal long LastCommitted;
    internal EventRange Range;

    internal CommandType Type;

    [Obsolete]
    internal static Command Completed(EventRange range)
    {
        return new Command { Range = range, Type = CommandType.RangeCompleted };
    }

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

    internal void Apply(ProjectionController controller)
    {
        switch (Type)
        {
            case CommandType.HighWater:
                controller.MarkHighWater(HighWaterMark);
                break;

            case CommandType.RangeCompleted:
                controller.EventRangeUpdated(Range);
                break;

            case CommandType.Start:
                controller.Start(HighWaterMark, LastCommitted);
                break;
        }
    }
}
