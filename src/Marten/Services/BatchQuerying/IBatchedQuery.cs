using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Linq;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchEvents
    {
        /// <summary>
        /// Fetch a live aggregation of a single event stream
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="streamId"></param>
        /// <param name="version"></param>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        Task<T> AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null) where T : class, new();


        /// <summary>
        /// Load a single event with all of its metadata
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<IEvent> Load(Guid id);
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
        Task<T> Load<T>(ValueType id) where T : class;

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
        Task<IList<T>> Query<T>(string sql, params object[] parameters) where T : class;

        /// <summary>
        /// Execute this batched query
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task Execute(CancellationToken token = default(CancellationToken));



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



    }
}