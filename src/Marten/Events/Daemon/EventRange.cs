using System.Collections.Generic;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Operations;

namespace Marten.Events.Daemon
{
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
            if (obj.GetType() != this.GetType()) return false;
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

        public ShardName ShardName { get; }
        public long SequenceFloor { get; }
        public long SequenceCeiling { get; }

        public IReadOnlyList<IEvent> Events { get; set; }
        public int Size => Events?.Count ?? (int)(SequenceCeiling - SequenceFloor);

        public IStorageOperation BuildProgressionOperation(EventGraph events)
        {
            if (SequenceFloor == 0) return new InsertProjectionProgress(events, this);

            return new UpdateProjectionProgress(events, this);
        }
    }
}
