using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services.BatchQuerying;
using Npgsql;

namespace Marten
{
    public interface IQuerySession : IDisposable
    {
        /// <summary>
        /// Find or load a single document of type T by a string id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        T Load<T>(string id) where T : class;

        /// <summary>
        /// Asynchronously find or load a single document of type T by a string id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<T> LoadAsync<T>(string id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Load or find a single document of type T with either a numeric or Guid id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        T Load<T>(int id) where T : class;

        /// <summary>
        /// Load or find a single document of type T with either a numeric or Guid id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        T Load<T>(long id) where T : class;

        /// <summary>
        /// Load or find a single document of type T with either a numeric or Guid id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        T Load<T>(Guid id) where T : class;

        /// <summary>
        /// Asynchronously load or find a single document of type T with either a numeric or Guid id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<T> LoadAsync<T>(int id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Asynchronously load or find a single document of type T with either a numeric or Guid id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<T> LoadAsync<T>(long id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Asynchronously load or find a single document of type T with either a numeric or Guid id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<T> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken)) where T : class;


        // SAMPLE: querying_with_linq
        /// <summary>
        /// Use Linq operators to query the documents
        /// stored in Postgresql
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IMartenQueryable<T> Query<T>();
        // ENDSAMPLE

        /// <summary>
        /// Queries the document storage table for the document type T by supplied SQL. See http://jasperfx.github.io/marten/documentation/documents/sql/ for more information on usage.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        IList<T> Query<T>(string sql, params object[] parameters);

        /// <summary>
        /// Asynchronously queries the document storage table for the document type T by supplied SQL. See http://jasperfx.github.io/marten/documentation/documents/sql/ for more information on usage.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="token"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        Task<IList<T>> QueryAsync<T>(string sql, CancellationToken token = default(CancellationToken), params object[] parameters);

        /// <summary>
        /// Define a batch of deferred queries and load operations to be conducted in one asynchronous request to the 
        /// database for potentially performance
        /// </summary>
        /// <returns></returns>
        IBatchedQuery CreateBatchQuery();

        /// <summary>
        /// The currently open Npgsql connection for this session. Use with caution.
        /// </summary>
        NpgsqlConnection Connection { get; }


        /// <summary>
        /// The session specific logger for this session. Can be set for better integration
        /// with custom diagnostics
        /// </summary>
        IMartenSessionLogger Logger { get; set; }

        /// <summary>
        /// Request count
        /// </summary>
        int RequestCount { get; }

        /// <summary>
        /// The document store that created this session
        /// </summary>
        IDocumentStore DocumentStore { get; }


        /// <summary>
        /// A query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
        /// </summary>
        /// <typeparam name="TDoc">The document</typeparam>
        /// <typeparam name="TOut">The output</typeparam>
        /// <param name="query">The instance of a compiled query</param>
        /// <returns>A single item query result</returns>
        TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query);

        /// <summary>
        /// An async query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
        /// </summary>
        /// <typeparam name="TDoc">The document</typeparam>
        /// <typeparam name="TOut">The output</typeparam>
        /// <param name="query">The instance of a compiled query</param>
        /// <param name="token">A cancellation token</param>
        /// <returns>A task for a single item query result</returns>
        Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default(CancellationToken));


        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IList<T> LoadMany<T>(params string[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IList<T> LoadMany<T>(params Guid[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IList<T> LoadMany<T>(params int[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IList<T> LoadMany<T>(params long[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(params string[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(params Guid[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(params int[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(params long[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params int[] ids) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids) where T : class;

        /// <summary>
        /// Directly load the persisted JSON data for documents by Id
        /// </summary>
        IJsonLoader Json { get; }

    }
}