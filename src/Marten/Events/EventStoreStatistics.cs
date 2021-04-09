#nullable enable
namespace Marten.Events
{
    public class EventStoreStatistics
    {
        /// <summary>
        /// Number of unique events in the event store table
        /// </summary>
        public long EventCount { get; set; }

        /// <summary>
        /// Number of unique streams in the event store
        /// </summary>
        public long StreamCount { get; set; }

        /// <summary>
        /// Current value of the event sequence. This may be higher than the number
        /// of events if events have been archived or if there were failures while
        /// appending events
        /// </summary>
        public long EventSequenceNumber { get; set; }
    }
}
