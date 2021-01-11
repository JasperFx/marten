using Marten.Events.Daemon.Progress;
using Marten.Internal.Operations;

namespace Marten.Events.Daemon
{
    internal class EventRange
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

        public string ProjectionOrShardName { get; }
        public long SequenceFloor { get; }
        public long SequenceCeiling { get; }

        public IStorageOperation BuildProgressionOperation(EventGraph events)
        {
            if (SequenceFloor == 0) return new InsertProjectionProgress(events, this);

            return new UpdateProjectionProgress(events, this);
        }
    }
}
