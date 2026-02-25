#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Internal.Sessions;
using StreamState = Marten.Events.StreamState;
using Marten.Linq;
using Marten.Linq.QueryHandlers;

namespace Marten.Services.BatchQuerying;

public interface IBatchEvents
{
    /// <summary>
    ///     Load a single event with all of its metadata
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    Task<IEvent> Load(Guid id);

    /// <summary>
    ///     Load the high level metadata about a single event stream
    /// </summary>
    /// <param name="streamId"></param>
    /// <returns></returns>
    Task<StreamState> FetchStreamState(Guid streamId);

    /// <summary>
    ///     Load the high level metadata about a single event stream
    /// </summary>
    /// <param name="streamKey"></param>
    /// <returns></returns>
    Task<StreamState> FetchStreamState(string streamKey);

    /// <summary>
    ///     Fetch all the events for a single event stream
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    /// <returns></returns>
    Task<IReadOnlyList<IEvent>> FetchStream(Guid streamId, long version = 0, DateTimeOffset? timestamp = null,
        long fromVersion = 0);

    /// <summary>
    ///     Fetch all the events for a single event stream
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    /// <returns></returns>
    Task<IReadOnlyList<IEvent>> FetchStream(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        long fromVersion = 0);

    /// <summary>
    ///     Fetch the projected aggregate T by id with built in optimistic concurrency checks
    ///     starting at the point the aggregate was fetched.
    /// </summary>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(Guid id) where T : class;

    /// <summary>
    ///     Fetch the projected aggregate T by id with built in optimistic concurrency checks
    ///     starting at the point the aggregate was fetched.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(string key) where T : class;

    /// <summary>
    ///     Fetch projected aggregate T by id and expected, current version of the aggregate. Will fail immediately
    ///     with ConcurrencyInjection if the expectedVersion is stale. Builds in optimistic concurrency for later
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(Guid id, long expectedVersion)
        where T : class;

    /// <summary>
    ///     Fetch projected aggregate T by id and expected, current version of the aggregate. Will fail immediately
    ///     with ConcurrencyInjection if the expectedVersion is stale. Builds in optimistic concurrency for later
    /// </summary>
    /// <param name="key"></param>
    /// <param name="expectedVersion"></param>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(string key, long expectedVersion)
        where T : class;

    /// <summary>
    ///     Fetch projected aggregate T by id for exclusive writing
    /// </summary>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForExclusiveWriting<T>(Guid id)
        where T : class;

    /// <summary>
    ///     Fetch projected aggregate T by id for exclusive writing
    /// </summary>
    /// <param name="key"></param>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForExclusiveWriting<T>(string key)
        where T : class;

    /// <summary>
    ///     Fetch the projected aggregate T by id. This API functions regardless of the projection lifecycle,
    /// and should be thought of as a lightweight, read-only version of FetchForWriting
    /// </summary>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<T?> FetchLatest<T>(Guid id) where T : class;

    /// <summary>
    ///     Fetch the projected aggregate T by id. This API functions regardless of the projection lifecycle,
    /// and should be thought of as a lightweight, read-only version of FetchForWriting
    /// </summary>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<T?> FetchLatest<T>(string id) where T : class;
}

public interface IBatchedQuery
{
    /// <summary>
    ///     Access to event store specific query mechanisms
    /// </summary>
    IBatchEvents Events { get; }

    QuerySession Parent { get; }

    /// <summary>
    ///     Load a single document of Type "T" by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    Task<T?> Load<T>(string id) where T : class;

    /// <summary>
    ///     Load a single document of Type "T" by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    Task<T?> Load<T>(int id) where T : class;

    /// <summary>
    ///     Load a single document of Type "T" by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    Task<T?> Load<T>(long id) where T : class;

    /// <summary>
    ///     Load a single document of Type "T" by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    Task<T?> Load<T>(Guid id) where T : class;

    /// <summary>
    ///     Load a single document of Type "T" by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    Task<T?> Load<T>(object id) where T : class;

    /// <summary>
    ///     Load a one or more documents of Type "T" by id's
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IBatchLoadByKeys<T> LoadMany<T>() where T : class;

    /// <summary>
    ///     Execute a user provided query against "T"
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<T>> Query<T>(string sql, params object[] parameters) where T : class;

    /// <summary>
    ///     Execute a user provided query against "T".
    ///      Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<T>> Query<T>(char placeholder, string sql, params object[] parameters) where T : class;

    /// <summary>
    ///     Execute this batched query
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    Task Execute(CancellationToken token = default);

    /// <summary>
    ///     Where for documents of type "T" by Linq expression
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IBatchedQueryable<T> Query<T>() where T : class;

    /// <summary>
    ///     Execute a compiled query as part of the batch query
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="query"></param>
    /// <returns></returns>
    Task<TResult> Query<TDoc, TResult>(ICompiledQuery<TDoc, TResult> query) where TDoc : class;

    /// <summary>
    /// Used internally by Marten. Allows for the usage of any old IQueryHandler<T>
    /// in a batch
    /// </summary>
    /// <param name="handler"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<T> AddItem<T>(IQueryHandler<T> handler);

    /// <summary>
    /// Enroll a query plan with a batch query
    /// </summary>
    /// <param name="plan"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<T> QueryByPlan<T>(IBatchQueryPlan<T> plan);

}
