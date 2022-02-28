using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace Marten.Events
{
    public interface IEventOperations
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
        StreamAction Append(Guid stream, long expectedVersion, params object[] events);

        /// <summary>
        /// Append one or more events in order to an existing stream and verify that maximum event id for the stream
        /// matches supplied expected version or transaction is aborted.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="expectedVersion">Expected maximum event version after append</param>
        /// <param name="events"></param>
        StreamAction Append(string stream, long expectedVersion, IEnumerable<object> events);

        /// <summary>
        /// Append one or more events in order to an existing stream and verify that maximum event id for the stream
        /// matches supplied expected version or transaction is aborted.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="expectedVersion">Expected maximum event version after append</param>
        /// <param name="events"></param>
        StreamAction Append(string stream, long expectedVersion, params object[] events);

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
    }

    public interface IEventStore: IEventOperations, IQueryEventStore
    {
        /// <summary>
        /// Append one or more events in order to an existing stream and verify that maximum event id for the stream
        /// matches supplied expected version or transaction is aborted.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="expectedVersion">Expected maximum event version after append</param>
        /// <param name="events"></param>
        StreamAction Append(Guid stream, long expectedVersion, IEnumerable<object> events);

        /// <summary>
        /// Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
        /// </summary>
        /// <typeparam name="TAggregate"></typeparam>
        /// <param name="id"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        StreamAction StartStream<TAggregate>(Guid id, IEnumerable<object> events) where TAggregate : class;

        /// <summary>
        /// Append events to an existing stream with optimistic concurrency checks against the
        /// existing version of the stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="token"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendOptimistic(string streamKey, CancellationToken token, params object[] events);

        /// <summary>
        /// Append events to an existing stream with optimistic concurrency checks against the
        /// existing version of the stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendOptimistic(string streamKey, params object[] events);

        /// <summary>
        /// Append events to an existing stream with optimistic concurrency checks against the
        /// existing version of the stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="token"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendOptimistic(Guid streamId, CancellationToken token, params object[] events);

        /// <summary>
        /// Append events to an existing stream with optimistic concurrency checks against the
        /// existing version of the stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendOptimistic(Guid streamId, params object[] events);

        /// <summary>
        /// Append events to an existing stream with an exclusive lock against the
        /// stream until this session is saved
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="token"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendExclusive(string streamKey, CancellationToken token, params object[] events);

        /// <summary>
        /// Append events to an existing stream with an exclusive lock against the
        /// stream until this session is saved
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendExclusive(string streamKey, params object[] events);

        /// <summary>
        /// Append events to an existing stream with an exclusive lock against the
        /// stream until this session is saved
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendExclusive(Guid streamId, CancellationToken token, params object[] events);

        /// <summary>
        /// Append events to an existing stream with an exclusive lock against the
        /// stream until this session is saved
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <exception cref="NonExistentStreamException"></exception>
        /// <returns></returns>
        Task AppendExclusive(Guid streamId, params object[] events);

        /// <summary>
        /// Mark a stream and all its events as archived
        /// </summary>
        /// <param name="streamId"></param>
        void ArchiveStream(Guid streamId);

        /// <summary>
        /// Mark a stream and all its events as archived
        /// </summary>
        /// <param name="streamKey"></param>
        void ArchiveStream(string streamKey);

        /// <summary>
        /// Apply a single header to multiple events in the current session.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="events"></param>
        /// <remarks>
        /// Metadata is applied after session-wide metadata and will override any conflicting keys/values.
        /// </remarks>
        void ApplyHeader(string key, object? value, params object[] events);

        /// <summary>
        /// Apply a collection of headers to multiple events in the current session.
        /// Setting value to null will remove the override.
        /// </summary>
        /// <param name="headers"></param>
        /// <param name="events"></param>
        /// <remarks>
        /// Metadata is applied after session-wide metadata and will override any conflicting keys/values.
        /// </remarks>
        void ApplyHeaders(IDictionary<string, object> headers, params object[] events);

        /// <summary>
        /// Apply a correlation id to multiple events in the current session.
        /// Setting to null will remove the override.
        /// </summary>
        /// <param name="headers"></param>
        /// <param name="events"></param>
        /// <remarks>
        /// Metadata is applied after session-wide metadata and will override any conflicting keys/values.
        /// </remarks>
        void ApplyCorrelationId(string? correlationId, params object[] events);

        /// <summary>
        /// Apply a causation id to multiple events in the current session.
        /// Setting to null will remove the override.
        /// </summary>
        /// <param name="headers"></param>
        /// <param name="events"></param>
        /// <remarks>
        /// Metadata is applied after session-wide metadata and will override any conflicting keys/values.
        /// </remarks>
        void ApplyCausationId(string? causationId, params object[] events);

        /// <summary>
        /// Apply metadata from an existing event to an event in the current session.
        /// CorrelationId, CausationId and Headers will be applied.
        /// </summary>
        /// <param name="headers"></param>
        /// <param name="events"></param>
        /// <remarks>
        /// Metadata is applied after session-wide metadata and will override any conflicting keys/values.
        /// </remarks>
        void CopyMetadata(IEventMetadata metadata, object @event);
    }
}
