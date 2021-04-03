using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Internal.Operations;
using Marten.Linq.SqlGeneration;
using Marten.Patching;
#nullable enable
namespace Marten
{
    /// <summary>
    /// Basic storage operations for document types, but cannot initiate any actual writes
    /// </summary>
    public interface IDocumentOperations: IQuerySession
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
        /// Explicitly marks multiple documents as needing to be inserted or updated upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Store<T>(IEnumerable<T> entities);

        /// <summary>
        /// Explicitly marks one or more documents as needing to be inserted or updated upon the next call to SaveChanges()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        void Store<T>(params T[] entities);

        /// <summary>
        /// Explicitly marks a document as needing to be updated and supplies the
        /// current known version for the purpose of optimistic versioning checks
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="version"></param>
        void Store<T>(T entity, Guid version);

        /// <summary>
        /// DocumentStore an enumerable of potentially mixed documents
        /// </summary>
        /// <param name="documents"></param>
        void StoreObjects(IEnumerable<object> documents);

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
        IPatchExpression<T> Patch<T>(ISqlFragment fragment);

        /// <summary>
        /// Catch all mechanism to add additional database calls to the batched
        /// updates in SaveChanges()/SaveChangesAsync()
        /// </summary>
        /// <param name="storageOperation"></param>
        void QueueOperation(IStorageOperation storageOperation);

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
        /// For soft-deleted document types, this is a one sized fits all mechanism to reverse the
        /// soft deletion tracking
        /// </summary>
        /// <param name="expression"></param>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        void UndoDeleteWhere<T>(Expression<Func<T, bool>> expression);
    }
}
