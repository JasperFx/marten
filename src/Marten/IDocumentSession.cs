using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Linq;
using Marten.Patching;
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
        void Delete<T>(int id);

        /// <summary>
        /// Mark an entity of type T with either a numeric or Guid id for deletion upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void Delete<T>(long id);

        /// <summary>
        /// Mark an entity of type T with either a numeric or Guid id for deletion upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        void Delete<T>(Guid id);

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


        /// <summary>
        /// Explicitly marks a document as needing to be inserted or updated upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Store<T>(params T[] entities);

        /// <summary>
        /// Explicitly marks a document as needing to be inserted or updated upon the next call to SaveChanges()
        /// to a specific tenant
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Store<T>(string tenantId, params T[] entities);

        /// <summary>
        /// Explicitly marks a document as needing to be updated and supplies the
        /// current known version for the purpose of optimistic versioning checks
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="version"></param>
        void Store<T>(T entity, Guid version);

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
        /// DocumentStore an enumerable of potentially mixed documents
        /// </summary>
        /// <param name="documents"></param>
        void StoreObjects(IEnumerable<object> documents);



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
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        IPatchExpression<T> Patch<T>(int id);

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        IPatchExpression<T> Patch<T>(long id);

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        IPatchExpression<T> Patch<T>(string id);

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        IPatchExpression<T> Patch<T>(Guid id);

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="where"></param>
        /// <returns></returns>
        IPatchExpression<T> Patch<T>(Expression<Func<T, bool>> where);

        /// <summary>
        /// Patch multiple documents matching the supplied where fragment
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fragment"></param>
        /// <returns></returns>
        IPatchExpression<T> Patch<T>(IWhereFragment fragment);

        /// <summary>
        /// Catch all mechanism to add additional database calls to the batched
        /// updates in SaveChanges()/SaveChangesAsync()
        /// </summary>
        /// <param name="storageOperation"></param>
        void QueueOperation(IStorageOperation storageOperation);


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
        Task<IReadOnlyList<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys, CancellationToken token = default(CancellationToken));
    }
}