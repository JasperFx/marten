using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Services;

namespace Marten
{
    /// <summary>
    /// Interface for querying a document database and unit of work updates
    /// </summary>
    public interface IDocumentSession: IDocumentOperations
    {
        /// <summary>
        /// Saves all the pending changes and deletions to the server in a single Postgresql transaction.
        /// </summary>
        void SaveChanges();

        /// <summary>
        /// Asynchronously saves all the pending changes and deletions to the server in a single Postgresql transaction
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task SaveChangesAsync(CancellationToken token = default);

        /// <summary>
        /// Explicitly marks a document as needing to be inserted upon the next call to SaveChanges().
        /// Will throw an exception if the document already exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Insert<T>(IEnumerable<T> entities);

        /// <summary>
        /// Explicitly marks a document as needing to be inserted upon the next call to SaveChanges().
        /// Will throw an exception if the document already exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Insert<T>(params T[] entities);

        /// <summary>
        /// Explicitly marks a document as needing to be updated upon the next call to SaveChanges().
        /// Will throw an exception if the document does not already exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Update<T>(IEnumerable<T> entities);

        /// <summary>
        /// Explicitly marks a document as needing to be updated upon the next call to SaveChanges().
        /// Will throw an exception if the document does not already exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Update<T>(params T[] entities);

        /// <summary>
        /// Insert an enumerable of potentially mixed documents. Will throw exceptions
        /// if a document overwrite is detected
        /// </summary>
        /// <param name="documents"></param>
        void InsertObjects(IEnumerable<object> documents);

        /// <summary>
        /// List of all the pending changes to this IDocumentSession
        /// </summary>
        IUnitOfWork PendingChanges { get; }

        /// <summary>
        /// Access to the event store functionality
        /// </summary>
        IEventStore Events { get; }

        /// <summary>
        /// Override whether or not this session honors optimistic concurrency checks
        /// </summary>
        ConcurrencyChecks Concurrency { get; }

        /// <summary>
        /// Writeable list of the listeners for this session
        /// </summary>
        IList<IDocumentSessionListener> Listeners { get; }

        /// <summary>
        /// Completely remove the document from this session's unit of work tracking and identity map caching
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="document"></param>
        void Eject<T>(T document);

        /// <summary>
        /// Completely remove all the documents of given type from this session's unit of work tracking and identity map caching
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        void EjectAllOfType(Type type);

        /// <summary>
        /// Optional metadata describing the causation id for this
        /// unit of work
        /// </summary>
        string CausationId { get; set; }

        /// <summary>
        /// Optional metadata describing the correlation id for this
        /// unit of work
        /// </summary>
        string CorrelationId { get; set; }

        /// <summary>
        /// Optional metadata describing the user name or
        /// process name for this unit of work
        /// </summary>
        string LastModifiedBy { get; set; }

        /// <summary>
        /// Set an optional user defined metadata value by key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        void SetHeader(string key, object value);

        /// <summary>
        /// Get an optional user defined metadata value by key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        object GetHeader(string key);


        /// <summary>
        /// Mark this entity for a "hard" deletion upon the next call to SaveChanges()
        /// that will delete the underlying database row
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void HardDelete<T>(T entity);

        /// <summary>
        /// Mark an entity of type T with either a numeric or Guid id for "hard" deletion upon the next call to SaveChanges()
        /// that will delete the underlying database row
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void HardDelete<T>(int id);

        /// <summary>
        /// Mark an entity of type T with either a numeric or Guid id for hard deletion upon the next call to SaveChanges()
        /// that will delete the underlying database row
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void HardDelete<T>(long id);

        /// <summary>
        /// Mark an entity of type T with either a numeric or Guid id for hard deletion upon the next call to SaveChanges()
        /// that will delete the underlying database row
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void HardDelete<T>(Guid id);

        /// <summary>
        /// Mark an entity of type T with a string id for hard deletion upon the next call to SaveChanges()
        /// that will delete the underlying database row
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void HardDelete<T>(string id);

        /// <summary>
        /// Bulk hard delete all documents of type T matching the expression condition
        /// that will delete the underlying database rows
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression"></param>
        void HardDeleteWhere<T>(Expression<Func<T, bool>> expression);

        /// <summary>
        /// Hard delete a supplied document in the named tenant id
        /// that will delete the underlying database row
        /// </summary>
        /// <param name="tenantId"></param>
        /// <param name="document"></param>
        /// <typeparam name="T"></typeparam>
        void HardDeleteInTenant<T>(string tenantId, T document);

        /// <summary>
        /// Hard delete a supplied document id and type in the named tenant id
        /// that will delete the underlying database row
        /// </summary>
        /// <param name="tenantId">The tenant id name</param>
        /// <param name="id">The document id</param>
        /// <typeparam name="T">The document type</typeparam>
        void HardDeleteByIdInTenant<T>(string tenantId, Guid id);

        /// <summary>
        /// Hard delete a supplied document id and type in the named tenant id
        /// that will delete the underlying database row
        /// </summary>
        /// <param name="tenantId">The tenant id name</param>
        /// <param name="id">The document id</param>
        /// <typeparam name="T">The document type</typeparam>
        void HardDeleteByIdInTenant<T>(string tenantId, int id);

        /// <summary>
        /// Hard delete a supplied document id and type in the named tenant id
        /// that will delete the underlying database row
        /// </summary>
        /// <param name="tenantId">The tenant id name</param>
        /// <param name="id">The document id</param>
        /// <typeparam name="T">The document type</typeparam>
        void HardDeleteByIdInTenant<T>(string tenantId, string id);

        /// <summary>
        /// Hard delete a supplied document id and type in the named tenant id
        /// that will delete the underlying database row
        /// </summary>
        /// <param name="tenantId">The tenant id name</param>
        /// <param name="id">The document id</param>
        /// <typeparam name="T">The document type</typeparam>
        void HardDeleteByIdInTenant<T>(string tenantId, long id);


        /// <summary>
        /// For soft-deleted document types, this is a one sized fits all mechanism to reverse the
        /// soft deletion tracking
        /// </summary>
        /// <param name="expression"></param>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        void UndoDeleteWhere<T>(Expression<Func<T, bool>> expression);
    }

    public interface ILoadByKeys<TDoc>
    {
        /// <summary>
        /// Supply the document id's to load
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        IReadOnlyList<TDoc> ById<TKey>(params TKey[] keys);

        /// <summary>
        /// Supply the document id's to load asynchronously
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task<IReadOnlyList<TDoc>> ByIdAsync<TKey>(params TKey[] keys);

        /// <summary>
        /// Supply the document id's to load
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        IReadOnlyList<TDoc> ById<TKey>(IEnumerable<TKey> keys);

        /// <summary>
        /// Supply the document id's to load asynchronously
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="keys"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IReadOnlyList<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys, CancellationToken token = default);

    }
}
