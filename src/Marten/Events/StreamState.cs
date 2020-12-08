using System;
using Marten.Schema.Identity;

namespace Marten.Events
{
    public class StreamState
    {
        public Guid Id { get; set; }
        public int Version { get; set;}
        public Type AggregateType { get; set;}

        public DateTime LastTimestamp { get; set;}
        public DateTime Created { get; set;}
        public string Key { get; set;}

        public StreamState()
        {
        }

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
            if (!(identifier is string) && !(identifier is Guid))
            {
                throw new ArgumentException("Stream identifier needs to be string or Guid type", nameof(identifier));
            }

            if (identifier is string)
            {
                return new StreamState((string)identifier, version, aggregateType, lastTimestamp, created);
            }
            else
            {
                return new StreamState((Guid)identifier, version, aggregateType, lastTimestamp, created);
            }
        }
    }
}
