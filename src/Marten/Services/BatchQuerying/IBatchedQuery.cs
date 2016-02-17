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
        /// Query for documents of type "T" by Linq expression
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IBatchedQueryable<T> Query<T>() where T : class;

    }
}