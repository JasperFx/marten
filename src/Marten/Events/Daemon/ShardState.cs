using System;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Point in time state of a single projection shard or the high water mark
    /// </summary>
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


        public ShardState(AsyncProjectionShard shard, long sequenceNumber) : this(shard.Name, sequenceNumber)
        {

        }

        public ShardState(AsyncProjectionShard shard, ShardAction action) : this(shard.Name, 0)
        {
            Action = action;
        }

        public ShardAction Action { get; set; } = ShardAction.Updated;

        /// <summary>
        /// Time this state was recorded
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Name of the projection shard
        /// </summary>
        public string ShardName { get; }

        /// <summary>
        /// Furthest event sequence number processed by this projection shard
        /// </summary>
        public long Sequence { get; }

        /// <summary>
        /// If not null, this is the exception that caused this state to be published
        /// </summary>
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
            if (obj.GetType() != GetType()) return false;
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
