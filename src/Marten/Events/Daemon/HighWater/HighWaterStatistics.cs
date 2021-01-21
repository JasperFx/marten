using System;

namespace Marten.Events.Daemon.HighWater
{
    internal class HighWaterStatistics
    {
        public long LastMark { get; set; }
        public long HighestSequence { get; set; }
        public long CurrentMark { get; set; }
        public bool HasChanged => CurrentMark > LastMark;
        public DateTimeOffset? LastUpdated { get; set; }
        public long SafeStartMark { get; set; }
        public DateTimeOffset Timestamp { get; set; }

        public HighWaterStatus InterpretStatus(HighWaterStatistics previous)
        {
            if (HighestSequence == 1 && CurrentMark == 0) return HighWaterStatus.CaughtUp;

            if (CurrentMark > previous.CurrentMark)
            {
                return CurrentMark == HighestSequence ? HighWaterStatus.CaughtUp : HighWaterStatus.Changed;
            }

            return HighWaterStatus.Stale;
        }
    }

    public enum HighWaterStatus
    {
        CaughtUp, // CurrentMark == HighestSequence, okay to pause
        Changed,  // The CurrentMark has progressed
        Stale,    // The CurrentMark isn't changing, but the sequence is ahead. Implies that there's some skips in the sequence
    }
}
