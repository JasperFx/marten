using System;

namespace Marten.Events.Daemon.HighWater;

internal class HighWaterStatistics
{
    public long LastMark { get; set; }
    public long HighestSequence { get; set; }
    public long CurrentMark { get; set; }
    public bool HasChanged => CurrentMark > LastMark;
    public DateTimeOffset? LastUpdated { get; set; }
    public long SafeStartMark { get; set; }
    public DateTimeOffset Timestamp { get; set; } = default;

    public HighWaterStatus InterpretStatus(HighWaterStatistics previous)
    {
        // Postgres sequences start w/ 1 by default. So the initial state is "HighestSequence = 1".
        if (HighestSequence == 1 && CurrentMark == 0)
        {
            return HighWaterStatus.CaughtUp;
        }

        if (CurrentMark == HighestSequence)
        {
            return HighWaterStatus.CaughtUp;
        }

        if (CurrentMark > previous.CurrentMark)
        {
            return HighWaterStatus.Changed;
        }

        return HighWaterStatus.Stale;
    }

    public bool TryGetStaleAge(out TimeSpan timeSinceUpdate)
    {
        if (LastUpdated.HasValue)
        {
            timeSinceUpdate = Timestamp.Subtract(LastUpdated.Value);
            return true;
        }

        timeSinceUpdate = default;
        return false;
    }
}

public enum HighWaterStatus
{
    CaughtUp, // CurrentMark == HighestSequence, okay to pause
    Changed, // The CurrentMark has progressed
    Stale // The CurrentMark isn't changing, but the sequence is ahead. Implies that there's some skips in the sequence
}
