using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Services;
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
        T Load<T>(ValueType id) where T : class;

        /// <summary>
        /// Asynchronously load or find a single document of type T with either a numeric or Guid id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<T> LoadAsync<T>(ValueType id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Load or find multiple documents by id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        ILoadByKeys<T> LoadMany<T>() where T : class;

        /// <summary>
        /// Load or find only the document json by string id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string FindJsonById<T>(string id) where T : class;

        /// <summary>
        /// Load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string FindJsonById<T>(ValueType id) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by string id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string> FindJsonByIdAsync<T>(string id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string> FindJsonByIdAsync<T>(ValueType id, CancellationToken token = default(CancellationToken)) where T : class;

        // SAMPLE: querying_with_linq
        /// <summary>
        /// Use Linq operators to query the documents
        /// stored in Postgresql
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IQueryable<T> Query<T>();
        // ENDSAMPLE

        /// <summary>
        /// Queries the document storage table for the document type T by supplied SQL. See http://jasperfx.github.io/marten/documentation/documents/sql/ for more information on usage.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        IEnumerable<T> Query<T>(string sql, params object[] parameters);

        /// <summary>
        /// Asynchronously queries the document storage table for the document type T by supplied SQL. See http://jasperfx.github.io/marten/documentation/documents/sql/ for more information on usage.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="token"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken token = default(CancellationToken), params object[] parameters);

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
        /// The store hosting the query session
        /// </summary>
        IDocumentStore Store { get; }
    }

    /// <summary>
    /// Interface for querying a document database and unit of work updates 
    /// </summary>
    public interface IDocumentSession : IQuerySession
    {
        /// <summary>
        /// Mark this entity for deletion upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Delete<T>(T entity);

        /// <summary>
        /// Mark an entity of type T with either a numeric or Guid id for deletion upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void Delete<T>(ValueType id);

        /// <summary>
        /// Mark an entity of type T with a string id for deletion upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void Delete<T>(string id);

        /// <summary>
        /// Bulk delete all documents of type T matching the expression condition
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression"></param>
        void DeleteWhere<T>(Expression<Func<T, bool>> expression);


        /// <summary>
        /// Saves all the pending changes and deletions to the server in a single Postgresql transaction.
        /// </summary>
        void SaveChanges();

        /// <summary>
        /// Asynchronously saves all the pending changes and deletions to the server in a single Postgresql transaction
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task SaveChangesAsync(CancellationToken token = default(CancellationToken));

        // TODO -- Store by etag? Version strategy?

        /// <summary>
        /// Explicitly marks a document as needing to be inserted or updated upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Store<T>(params T[] entities);


        /// <summary>
        /// List of all the pending changes to this IDocumentSession
        /// </summary>
        IUnitOfWork PendingChanges { get; }

        /// <summary>
        /// Store an enumerable of potentially mixed documents
        /// </summary>
        /// <param name="documents"></param>
        void StoreObjects(IEnumerable<object> documents);


        /// <summary>
        /// Access to the event store functionality
        /// </summary>
        IEventStore Events { get; }

        /// <summary>
        /// A history of the commits for this session in 
        /// order of commits
        /// </summary>
        IEnumerable<IChangeSet> Commits { get; }

        /// <summary>
        /// The last set of changes committed, if any
        /// </summary>
        IChangeSet LastCommit { get; }
    }

    public interface ILoadByKeys<TDoc>
    {
        /// <summary>
        /// Supply the document id's to load
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        IList<TDoc> ById<TKey>(params TKey[] keys);

        /// <summary>
        /// Supply the document id's to load asynchronously
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task<IList<TDoc>> ByIdAsync<TKey>(params TKey[] keys);

        /// <summary>
        /// Supply the document id's to load
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        IList<TDoc> ById<TKey>(IEnumerable<TKey> keys);

        /// <summary>
        /// Supply the document id's to load asynchronously
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IList<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys, CancellationToken token = default(CancellationToken));
    }
}