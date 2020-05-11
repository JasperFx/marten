using System;
using System.Collections.Generic;

namespace Marten.V4
{
    public class MyMetadata
    {
        public Guid CorrelationId { get; set; }
        public string UserId { get; set; }
    }

    public enum StreamState
    {
        CreateNew,
        Append,
        AppendOrCreate,
        Pending, // Not sure this is necessary
        History
    }

    // Similar to Refit. Marten would whip up an actual implementation
    // for this by inheriting from the base DocumentSession, and adding
    // implementations for the event collection.
    public interface IMyDocumentSession : IDocumentSession
    {
        EventCollection<string, object> Events { get; }

        // Could be more than one Event collection here as an equivalent
        // to stream type.
    }

    // Thinking the document session would smuggle the metadata
    // to the event collection at SaveChanges() time

    // You *could* bake in optimistic concurrency per stream here

    // *These types* would be codegenerated ala Refit
    public abstract class EventCollection<TKey, TEventBase>
    {
        // The event stream would have to already exist and would throw
        // if the stream does not already exist
        public abstract void AppendToExistingStream(TKey streamId, params TEventBase[] events);

        // Effectively an append or create
        public abstract void Append(TKey streamId, params TEventBase[] events);

        // If you already know what the stream id should be
        public abstract void StartStreamWithId(TKey streamId, params TEventBase[] events);
        public abstract TKey StartStream(params TEventBase[] events);

        public abstract EventSlice<TKey> FetchStream(TKey key);

        // Now, this would either try to do a lock against the stream
        // row so only this session could edit it...
        public abstract void LockStream(TKey streamId);

    }

    // This really represents a segment of the underlying stream
    // Either from being fetched, or pending

    // You'd see this as part of the unit of work, or by querying the event
    // streams
    public abstract class EventSlice<TKey>
    {
        public TKey Id { get; set; }

        // There's a little bit of optimization for inline
        // projections by doing this

        // Other folks want different mechanics
        public StreamState State { get; set; }

        public IList<V4IEvent<TKey>> Events { get; } = new List<V4IEvent<TKey>>();

        // This would maybe be calculated
        public int FromVersion { get; }

        // This would maybe be calculated
        public int ToVersion { get; }
    }

    public interface V4IEvent<TKey>
    {
        object Data { get; }

        /// <summary>
        /// Not 100% sure you'd keep this on this class
        /// </summary>
        TKey StreamId { get; }

        /// <summary>
        /// Globally unique event like you'd expect
        /// </summary>
        Guid Id { get;  }

        /// <summary>
        /// Version number within the Stream
        /// </summary>
        int Version { get; }

        /// <summary>
        /// Sequence within the EventCollection
        /// </summary>
        long Sequence { get;  }

        DateTimeOffset Timestamp { get;  }
        object Metadata { get; set; }
    }

    // Basically the same as <= v3, but the StreamId would be variable
    public class V4Event<TKey, TEvent> : V4IEvent<TKey>
    {
        /// <summary>
        /// Not 100% sure you'd keep this on this class
        /// </summary>
        public TKey StreamId { get; set; }

        /// <summary>
        /// Globally unique event like you'd expect
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Version number within the Stream
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Sequence within the EventCollection
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        /// The actual event type.
        /// </summary>
        public TEvent Data { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        // Can be basically anything that can be
        // json serialized
        public object Metadata { get; set; }

        object V4IEvent<TKey>.Data => Data;
    }



}
