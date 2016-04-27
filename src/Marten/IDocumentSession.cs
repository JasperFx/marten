using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Linq.QueryHandlers;
using Marten.Services;

namespace Marten
{
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

        // TODO -- DocumentStore by etag? Version strategy?

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
        /// DocumentStore an enumerable of potentially mixed documents
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