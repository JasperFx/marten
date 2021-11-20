using System;
#nullable enable
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
        public long Version { get; set;}

        /// <summary>
        /// If the stream was started as tagged to an aggregate type, that will
        /// be reflected in this property. May be null
        /// </summary>
        public Type AggregateType { get; set;}

        /// <summary>
        /// The last time this stream was appended to
        /// </summary>
        public DateTimeOffset LastTimestamp { get; set;}

        /// <summary>
        /// The time at which this stream was created
        /// </summary>
        public DateTimeOffset Created { get; set;}

        /// <summary>
        /// The identity of this stream if using strings as the stream
        /// identity
        /// </summary>
        public string? Key { get; set;}

        /// <summary>
        /// Is this event stream marked as archived
        /// </summary>
        public bool IsArchived { get; set; }

        // This needs to stay public
#nullable disable
        public StreamState()
        {
        }
#nullable enable

        public StreamState(Guid id, long version, Type aggregateType, DateTimeOffset lastTimestamp, DateTimeOffset created)
        {
            Id = id;
            Version = version;
            AggregateType = aggregateType;
            LastTimestamp = lastTimestamp;
            Created = created;
        }

        public StreamState(string key, long version, Type aggregateType, DateTimeOffset lastTimestamp, DateTimeOffset created)
        {
            Key = key;
            Version = version;
            AggregateType = aggregateType;
            LastTimestamp = lastTimestamp;
            Created = created;
        }

    }
}
