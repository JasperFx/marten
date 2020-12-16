using System;
using Marten.Schema.Identity;

namespace Marten.Events
{
    public class StreamState
    {
        /// <summary>
        /// Identity of the stream if using Guid's as the identity
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Current version of the stream in the database. Corresponds to
        /// the number of events in the stream
        /// </summary>
        public int Version { get; set;}

        /// <summary>
        /// If the stream was started as tagged to an aggregate type, that will
        /// be reflected in this property. May be null
        /// </summary>
        public Type AggregateType { get; set;}

        /// <summary>
        /// The last time this stream was appended to
        /// </summary>
        public DateTime LastTimestamp { get; set;}

        /// <summary>
        /// The time at which this stream was created
        /// </summary>
        public DateTime Created { get; set;}

        /// <summary>
        /// The identity of this stream if using strings as the stream
        /// identity
        /// </summary>
        public string Key { get; set;}

        // This needs to stay public
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
