using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQuery
    {
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
        /// Query for the existence of any documents of type "T" matching the query
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<bool> Any<T>(Func<IQueryable<T>, IQueryable<T>> query);

        /// <summary>
        /// Query for the existence of any documents of type "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> Any<T>();

        /// <summary>
        /// Return a count of all the documents of type "T" that match the query
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<long> Count<T>(Func<IQueryable<T>, IQueryable<T>> query);

        /// <summary>
        /// Return a count of all the documents of type "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<long> Count<T>();

        /// <summary>
        /// Query for documents of type "T" by Linq expression
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<IList<T>> Query<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class;

        /// <summary>
        /// Query for *all* of the documents of type "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> QueryAll<T>() where T : class;

        /// <summary>
        /// Find the first document of type "T" matching this query. Will throw an exception if there are no matching documents
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<T> First<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class;

        /// <summary>
        /// Find the first document of type "T" that matches the query. Will return null if no documents match.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<T> FirstOrDefault<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class;

        /// <summary>
        /// Returns the single document of type "T" matching this query. Will 
        /// throw an exception if the results are null or contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<T> Single<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class;

        /// <summary>
        /// Returns the single document of type "T" matching this query or null. Will 
        /// throw an exception if the results contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        Task<T> SingleOrDefault<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class;

    }
}