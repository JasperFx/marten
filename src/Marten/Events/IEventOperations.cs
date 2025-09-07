using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;

namespace Marten.Events;

public interface IEventOperations
{
    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(Guid stream, IEnumerable<object> events);

    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(Guid stream, params object[] events);

    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(string stream, IEnumerable<object> events);

    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(string stream, params object[] events);

    /// <summary>
    ///     Append one or more events in order to an existing stream and verify that maximum event id for the stream
    ///     matches supplied expected version or transaction is aborted.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="expectedVersion">Expected maximum event version after append</param>
    /// <param name="events"></param>
    StreamAction Append(Guid stream, long expectedVersion, params object[] events);

    /// <summary>
    ///     Append one or more events in order to an existing stream and verify that maximum event id for the stream
    ///     matches supplied expected version or transaction is aborted.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="expectedVersion">Expected maximum event version after append</param>
    /// <param name="events"></param>
    StreamAction Append(string stream, long expectedVersion, IEnumerable<object> events);

    /// <summary>
    ///     Append one or more events in order to an existing stream and verify that maximum event id for the stream
    ///     matches supplied expected version or transaction is aborted.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="expectedVersion">Expected maximum event version after append</param>
    /// <param name="events"></param>
    StreamAction Append(string stream, long expectedVersion, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, Guid id, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, Guid id, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied string ID  and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(string streamKey, IEnumerable<object> events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, string streamKey, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, string streamKey, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream - WILL
    ///     THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Guid id, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream - WILL
    ///     THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Guid id, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(string streamKey, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(string streamKey, params object[] events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(params object[] events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, params object[] events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(params object[] events);

    /// <summary>
    /// Compact a stream by replacing its first event with a Compacted<T> event that establishes
    /// the snapshot. Do this when you do not care about older stream data, but do want to
    /// keep the database size down for better performance
    /// </summary>
    /// <param name="streamKey">The string identifier for the stream</param>
    /// <param name="configure">Configure the compacting request. Default is to compact at the latest point</param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task CompactStreamAsync<T>(string streamKey, Action<StreamCompactingRequest<T>>? configure = null);
    Task CompactStreamAsync<T>(Guid streamId, Action<StreamCompactingRequest<T>>? configure = null);
}
