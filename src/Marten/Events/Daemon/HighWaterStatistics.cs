using System;

namespace Marten.Events.Daemon
{
    internal class HighWaterStatistics
    {
        public long LastMark { get; set; }
        public long HighestSequence { get; set; }
        public long CurrentMark { get; set; }
        public bool HasChanged => CurrentMark > LastMark;
        public DateTimeOffset? LastUpdated { get; set; }
    }
}