using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Linq;
#nullable enable
namespace Marten.Services.BatchQuerying
{
    public interface IBatchEvents
    {

        /// <summary>
        /// Load a single event with all of its metadata
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<IEvent> Load(Guid id);

        /// <summary>
        /// Load the high level metadata about a single event stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <returns></returns>
        Task<StreamState> FetchStreamState(Guid streamId);

        /// <summary>
        /// Fetch all the events for a single event stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="version"></param>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        Task<IReadOnlyList<IEvent>> FetchStream(Guid streamId, long version = 0, DateTime? timestamp = null);
    }

    public interface IBatchedQuery
    {
        /// <summary>
        /// Access to event store specific query mechanisms
        /// </summary>
        IBatchEvents Events { get; }

        /// <summary>
        /// Load a single document of Type "T" by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> Load<T>(string id) where T : class;

        /// <summary>
        /// Load a single document of Type "T" by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> Load<T>(int id) where T : class;

        /// <summary>
        /// Load a single document of Type "T" by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> Load<T>(long id) where T : class;

        /// <summary>
        /// Load a single document of Type "T" by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> Load<T>(Guid id) where T : class;

        /// <summary>
        /// Load a one or more documents of Type "T" by id's
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IBatchLoadByKeys<T> LoadMany<T>() where T : class;

        /// <summary>
        /// Execute a user provided query against "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        Task<IReadOnlyList<T>> Query<T>(string sql, params object[] parameters) where T : class;

        /// <summary>
        /// Execute this batched query
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task Execute(CancellationToken token = default);

        /// <summary>
        /// Where for documents of type "T" by Linq expression
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IBatchedQueryable<T> Query<T>() where T : class;

        /// <summary>
        /// Execute a compiled query as part of the batch query
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<TResult> Query<TDoc, TResult>(ICompiledQuery<TDoc, TResult> query);

        /// <summary>
        /// Force the batched query to execute synchronously
        /// </summary>
        void ExecuteSynchronously();
    }
}
