﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal.Sessions;
using Marten.Linq;

namespace Marten.Events;

public interface IQueryEventStore
{
    /// <summary>
    ///     Synchronously fetches all of the events for the named stream
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, long version = 0, DateTimeOffset? timestamp = null,
        long fromVersion = 0, CancellationToken token = default);

    /// <summary>
    ///     Synchronously fetches all of the events for the named stream
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        long fromVersion = 0, CancellationToken token = default);

    /// <summary>
    ///     Perform a live aggregation of the raw events in this stream to a T object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="streamId"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    /// <param name="state">Instance of T to apply events to</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> AggregateStreamAsync<T>(Guid streamId, long version = 0, DateTimeOffset? timestamp = null, T? state = null,
        long fromVersion = 0, CancellationToken token = default) where T : class;

    /// <summary>
    ///     Perform a live aggregation of the raw events in this stream to a T object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="streamKey"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    /// <param name="state">Instance of T to apply events to</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> AggregateStreamAsync<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        T? state = null, long fromVersion = 0, CancellationToken token = default) where T : class;

    /// <summary>
    ///     Perform a live aggregation of the raw events in this stream to a T object, but return the last known
    /// version of the aggregate in case the aggregate itself is marked as deleted at a specific version
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="streamId"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> AggregateStreamToLastKnownAsync<T>(Guid streamId, long version = 0, DateTimeOffset? timestamp = null,
        CancellationToken token = default) where T : class;

    /// <summary>
    /// Perform a live aggregation of the raw events in this stream to a T object, but return the last known
    /// version of the aggregate in case the aggregate itself is marked as deleted at a specific version
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="streamKey"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> AggregateStreamToLastKnownAsync<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        CancellationToken token = default) where T : class;

    /// <summary>
    ///     Query directly against ONLY the raw event data. Use IQuerySession.Query() for aggregated documents!
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> QueryRawEventDataOnly<T>() where T : notnull;

    /// <summary>
    ///     Query directly against the raw event data across all event types
    /// </summary>
    /// <returns></returns>
    IMartenQueryable<IEvent> QueryAllRawEvents();

    /// <summary>
    ///     Load a single event by its id knowing the event type upfront
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IEvent<T>?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : class;

    /// <summary>
    ///     Load a single event by its id
    /// </summary>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IEvent?> LoadAsync(Guid id, CancellationToken token = default);

    /// <summary>
    ///     Fetches only the metadata about a stream by id
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<StreamState?> FetchStreamStateAsync(Guid streamId, CancellationToken token = default);

    /// <summary>
    ///     Fetches only the metadata about a stream by id
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<StreamState?> FetchStreamStateAsync(string streamKey, CancellationToken token = default);
}
