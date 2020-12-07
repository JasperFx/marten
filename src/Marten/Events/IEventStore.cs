using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;

namespace Marten.Events
{
    public interface IEventStore
    {
        /// <summary>
        /// Append one or more events in order to an existing stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="events"></param>
        StreamAction Append(Guid stream, IEnumerable<object> events);

        /// <summary>
        /// Append one or more events in order to an existing stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="events"></param>
        StreamAction Append(Guid stream, params object[] events);

        /// <summary>
        /// Append one or more events in order to an existing stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="events"></param>
        StreamAction Append(string stream, IEnumerable<object> events);

        /// <summary>
        /// Append one or more events in order to an existing stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="events"></param>
        StreamAction Append(string stream, params object[] events);

        /// <summary>
        /// Append one or more events in order to an existing stream and verify that maximum event id for the stream
        /// matches supplied expected version or transaction is aborted.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="expectedVersion">Expected maximum event version after append</param>
        /// <param name="events"></param>
        StreamAction Append(Guid stream, int expectedVersion, IEnumerable<object> events);

        /// <summary>
        /// Append one or more events in order to an existing stream and verify that maximum event id for the stream
        /// matches supplied expected version or transaction is aborted.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="expectedVersion">Expected maximum event version after append</param>
        /// <param name="events"></param>
        StreamAction Append(Guid stream, int expectedVersion, params object[] events);

        /// <summary>
        /// Append one or more events in order to an existing stream and verify that maximum event id for the stream
        /// matches supplied expected version or transaction is aborted.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="expectedVersion">Expected maximum event version after append</param>
        /// <param name="events"></param>
        StreamAction Append(string stream, int expectedVersion, IEnumerable<object> events);

        /// <summary>
        /// Append one or more events in order to an existing stream and verify that maximum event id for the stream
        /// matches supplied expected version or transaction is aborted.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="expectedVersion">Expected maximum event version after append</param>
        /// <param name="events"></param>
        StreamAction Append(string stream, int expectedVersion, params object[] events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="id"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream<TAggregate>(Guid id, IEnumerable<object> events) where TAggregate : class;

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="id"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class;

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        /// </summary>
        /// <param name="aggregateType"></param>
        /// <param name="id"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Type aggregateType, Guid id, IEnumerable<object> events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        /// </summary>
        /// <param name="aggregateType"></param>
        /// <param name="id"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Type aggregateType, Guid id, params object[] events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="streamKey">String identifier of this stream</param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream<TAggregate>(string streamKey, IEnumerable<object> events) where TAggregate : class;

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="streamKey">String identifier of this stream</param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class;

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <param name="aggregateType"></param>
        /// <param name="streamKey">String identifier of this stream</param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Type aggregateType, string streamKey, IEnumerable<object> events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <param name="aggregateType"></param>
        /// <param name="streamKey">String identifier of this stream</param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Type aggregateType, string streamKey, params object[] events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <param name="id"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Guid id, IEnumerable<object> events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <param name="id"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Guid id, params object[] events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(string streamKey, IEnumerable<object> events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(string streamKey, params object[] events);

        /// <summary>
        /// Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class;

        /// <summary>
        /// Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream<TAggregate>(params object[] events) where TAggregate : class;

        /// <summary>
        /// Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Type aggregateType, IEnumerable<object> events);

        /// <summary>
        /// Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(Type aggregateType, params object[] events);

        /// <summary>
        /// Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(IEnumerable<object> events);

        /// <summary>
        /// Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
        ///  - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream(params object[] events);

        /// <summary>
        /// Synchronously fetches all of the events for the named stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="version">If set, queries for events up to and including this version</param>
        /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
        /// <returns></returns>
        IReadOnlyList<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null);

        /// <summary>
        /// Synchronously fetches all of the events for the named stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="version">If set, queries for events up to and including this version</param>
        /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null, CancellationToken token = default);

        /// <summary>
        /// Synchronously fetches all of the events for the named stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="version">If set, queries for events up to and including this version</param>
        /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
        /// <returns></returns>
        IReadOnlyList<IEvent> FetchStream(string streamKey, int version = 0, DateTime? timestamp = null);

        /// <summary>
        /// Synchronously fetches all of the events for the named stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="version">If set, queries for events up to and including this version</param>
        /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, int version = 0, DateTime? timestamp = null, CancellationToken token = default);

        /// <summary>
        /// Perform a live aggregation of the raw events in this stream to a T object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="streamId"></param>
        /// <param name="version"></param>
        /// <param name="timestamp"></param>
        /// <param name="state">Instance of T to apply events to</param>
        /// <returns></returns>
        T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null, T state = null) where T : class;

        /// <summary>
        /// Perform a live aggregation of the raw events in this stream to a T object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="streamId"></param>
        /// <param name="version"></param>
        /// <param name="timestamp"></param>
        /// <param name="state">Instance of T to apply events to</param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<T> AggregateStreamAsync<T>(Guid streamId, int version = 0, DateTime? timestamp = null, T state = null, CancellationToken token = default) where T : class;

        /// <summary>
        /// Perform a live aggregation of the raw events in this stream to a T object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="streamKey"></param>
        /// <param name="version"></param>
        /// <param name="timestamp"></param>
        /// <param name="state">Instance of T to apply events to</param>
        /// <returns></returns>
        T AggregateStream<T>(string streamKey, int version = 0, DateTime? timestamp = null, T state = null) where T : class;

        /// <summary>
        /// Perform a live aggregation of the raw events in this stream to a T object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="streamKey"></param>
        /// <param name="version"></param>
        /// <param name="timestamp"></param>
        /// <param name="state">Instance of T to apply events to</param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<T> AggregateStreamAsync<T>(string streamKey, int version = 0, DateTime? timestamp = null, T state = null, CancellationToken token = default) where T : class;

        /// <summary>
        /// Query directly against ONLY the raw event data. Use IQuerySession.Query() for aggregated documents!
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IMartenQueryable<T> QueryRawEventDataOnly<T>();

        /// <summary>
        /// Query directly against the raw event data across all event types
        /// </summary>
        /// <returns></returns>
        IMartenQueryable<IEvent> QueryAllRawEvents();

        /// <summary>
        /// Load a single event by its id knowing the event type upfront
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Event<T> Load<T>(Guid id) where T : class;

        /// <summary>
        /// Load a single event by its id knowing the event type upfront
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<Event<T>> LoadAsync<T>(Guid id, CancellationToken token = default) where T : class;

        /// <summary>
        /// Load a single event by its id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        IEvent Load(Guid id);

        /// <summary>
        /// Load a single event by its id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IEvent> LoadAsync(Guid id, CancellationToken token = default);

        /// <summary>
        /// Fetches only the metadata about a stream by id
        /// </summary>
        /// <param name="streamId"></param>
        /// <returns></returns>
        StreamState FetchStreamState(Guid streamId);

        /// <summary>
        /// Fetches only the metadata about a stream by id
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = default);

        /// <summary>
        /// Fetches only the metadata about a stream by id
        /// </summary>
        /// <param name="streamKey"></param>
        /// <returns></returns>
        StreamState FetchStreamState(string streamKey);

        /// <summary>
        /// Fetches only the metadata about a stream by id
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<StreamState> FetchStreamStateAsync(string streamKey, CancellationToken token = default);
    }
}
