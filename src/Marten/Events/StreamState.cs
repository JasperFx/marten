using System;

namespace Marten.Events
{
    public class StreamState
    {
        public Guid Id { get; }
        public int Version { get; }
        public Type AggregateType { get; }

        public DateTime LastTimestamp { get; }

        public StreamState(Guid id, int version, Type aggregateType, DateTime lastTimestamp)
        {
            Id = id;
            Version = version;
            AggregateType = aggregateType;
            LastTimestamp = lastTimestamp;
        }
    }
}