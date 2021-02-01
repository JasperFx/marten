using System;

namespace Marten.Events.Daemon
{
    public class ShardState
    {
        public const string HighWaterMark = "HighWaterMark";
        public const string AllProjections = "AllProjections";

        public ShardState(string shardName, long sequence)
        {
            ShardName = shardName;
            Sequence = sequence;
            Timestamp = DateTimeOffset.UtcNow;
        }

        public ShardState(ShardName shardName, long sequence) : this(shardName.Identity, sequence)
        {

        }


        public ShardState(IAsyncProjectionShard shard, long sequenceNumber) : this(shard.Name, sequenceNumber)
        {

        }

        public ShardState(IAsyncProjectionShard shard, ShardAction action) : this(shard.Name, 0)
        {
            Action = action;
        }

        public ShardAction Action { get; set; } = ShardAction.Updated;

        public DateTimeOffset Timestamp { get; }

        public string ShardName { get; }
        public long Sequence { get; }
        public Exception Exception { get; set; }

        public override string ToString()
        {
            return $"{nameof(ShardName)}: {ShardName}, {nameof(Sequence)}: {Sequence}, {nameof(Action)}: {Action}";
        }

        protected bool Equals(ShardState other)
        {
            return ShardName == other.ShardName && Sequence == other.Sequence;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ShardState) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ShardName != null ? ShardName.GetHashCode() : 0) * 397) ^ Sequence.GetHashCode();
            }
        }
    }
}
