using System;
using Marten.Schema.Identity;

namespace Marten.Events
{
    public class StreamState
    {
        public Guid Id { get; } = CombGuidIdGeneration.NewGuid();
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

        internal static StreamState Create(object identifier, int version, Type aggregateType, DateTime lastTimestamp, DateTime created)
        {
            if (identifier is string stringValue)
            {
                return new StreamState(stringValue, version, aggregateType, lastTimestamp, created);
            }

            if (identifier is Guid guidValue)
            {
                return new StreamState(guidValue, version, aggregateType, lastTimestamp, created);
            }

            throw new ArgumentException("Stream identifier needs to be string or Guid type", nameof(identifier));
        }
    }
}
