using System;

namespace Marten.Events
{
    public class StreamState
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int Version { get; }
        public Type AggregateType { get; }

        public DateTime LastTimestamp { get; }
        public DateTime Created { get; }
        public string Key { get; }

        public StreamState(Guid id, int version, Type aggregateType, DateTime lastTimestamp, DateTime created)
        {
            Id = id;
            Version = version;
            AggregateType = aggregateType;
            LastTimestamp = lastTimestamp;
            Created = created;
        }

        public StreamState(string key, int version, Type aggregateType, DateTime lastTimestamp, DateTime created)
        {
            Key = key;
            Version = version;
            AggregateType = aggregateType;
            LastTimestamp = lastTimestamp;
            Created = created;
        }
    }
}