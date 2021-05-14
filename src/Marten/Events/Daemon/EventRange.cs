using System.Collections.Generic;
using System.Linq;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Operations;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Used to specify then track a range of events by sequence number
    /// within the asynchronous projections
    /// </summary>
    public class EventRange
    {
        public EventRange(ShardName shardName, long floor, long ceiling)
        {
            ShardName = shardName;
            SequenceFloor = floor;
            SequenceCeiling = ceiling;
        }

        public EventRange(ShardName shardName, long ceiling)
        {
            ShardName = shardName;
            SequenceCeiling = ceiling;
        }

        protected bool Equals(EventRange other)
        {
            return Equals(ShardName, other.ShardName) && SequenceFloor == other.SequenceFloor && SequenceCeiling == other.SequenceCeiling;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((EventRange) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (ShardName != null ? ShardName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ SequenceFloor.GetHashCode();
                hashCode = (hashCode * 397) ^ SequenceCeiling.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"Event range of '{ShardName}', {SequenceFloor} to {SequenceCeiling}";
        }

        /// <summary>
        /// Identifies the projection shard consuming this event range
        /// </summary>
        public ShardName ShardName { get; }

        /// <summary>
        /// The non-inclusive lower bound of the event sequence numbers
        /// in this range
        /// </summary>
        public long SequenceFloor { get; }

        /// <summary>
        /// The inclusive upper bound of the event sequence numbers in this range
        /// </summary>
        public long SequenceCeiling { get; }

        /// <summary>
        /// The actual events fetched for this range and the base filters of the projection
        /// shard
        /// </summary>
        public List<IEvent> Events { get; set; }

        /// <summary>
        /// The actual number of events in this range
        /// </summary>
        public int Size => Events?.Count ?? (int)(SequenceCeiling - SequenceFloor);

        internal IStorageOperation BuildProgressionOperation(EventGraph events)
        {
            if (SequenceFloor == 0) return new InsertProjectionProgress(events, this);

            return new UpdateProjectionProgress(events, this);
        }

        public void SkipEventSequence(long eventSequence)
        {
            var events = Events.ToList();
            events.RemoveAll(e => e.Sequence == eventSequence);
            Events = events;
        }
    }
}
