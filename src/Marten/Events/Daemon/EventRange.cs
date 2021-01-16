using System.Collections.Generic;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Operations;

namespace Marten.Events.Daemon
{
    public class EventRange
    {
        public EventRange(string projectionOrShardName, long floor, long ceiling)
        {
            ProjectionOrShardName = projectionOrShardName;
            SequenceFloor = floor;
            SequenceCeiling = ceiling;
        }

        public EventRange(string projectionOrShardName, long sequenceCeiling)
        {
            ProjectionOrShardName = projectionOrShardName;
            SequenceCeiling = sequenceCeiling;
        }

        protected bool Equals(EventRange other)
        {
            return ProjectionOrShardName == other.ProjectionOrShardName && SequenceFloor == other.SequenceFloor && SequenceCeiling == other.SequenceCeiling;
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
                var hashCode = (ProjectionOrShardName != null ? ProjectionOrShardName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ SequenceFloor.GetHashCode();
                hashCode = (hashCode * 397) ^ SequenceCeiling.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"Event range of '{ProjectionOrShardName}', {SequenceFloor} to {SequenceCeiling}";
        }

        public string ProjectionOrShardName { get; }
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
