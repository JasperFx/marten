using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Internal.Operations;
using Marten.Patching;
#nullable enable
namespace Marten.Services
{
    public interface IUnitOfWork
    {
        /// <summary>
        /// All of the pending deletions that will be processed
        /// when this session is committed
        /// </summary>
        /// <returns></returns>
        IEnumerable<IDeletion> Deletions();

        /// <summary>
        /// All the pending deletions of documents of type T that will be processed
        /// when this session is committed
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<IDeletion> DeletionsFor<T>();

        /// <summary>
        /// All the pending deletions of documents of type documentType that will be processed
        /// when this session is committed
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        IEnumerable<IDeletion> DeletionsFor(Type documentType);

        /// <summary>
        /// All the documents that will be updated when this session is committed
        /// This is inclusive of both Upsert and Updates
        /// </summary>
        /// <returns></returns>
        IEnumerable<object> Updates();

        /// <summary>
        /// All of the documents that will be inserted when this session is committed
        /// </summary>
        /// <returns></returns>
        IEnumerable<object> Inserts();

        /// <summary>
        /// All the documents of type T that will be updated when this session is committed.
        /// This is inclusive of both Upsert and Updates
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<T> UpdatesFor<T>();

        /// <summary>
        /// All the documents of type T that will be inserted when this session is committed
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<T> InsertsFor<T>();

        /// <summary>
        /// All of the documents of type T that will be inserted or updated when this session
        /// is committed
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<T> AllChangedFor<T>();

        /// <summary>
        /// All of the pending events for the event store in this unit of work
        /// </summary>
        /// <returns></returns>
        IList<StreamAction> Streams();

        /// <summary>
        /// All of the pending patch operations in this unit of work
        /// </summary>
        /// <returns></returns>
        IEnumerable<PatchOperation> Patches();

        /// <summary>
        /// All the storage operations that will be executed when this session is committed
        /// </summary>
        /// <returns></returns>
        IEnumerable<IStorageOperation> Operations();

        /// <summary>
        /// All the storage operations that will be executed for documents of type T when this
        /// session is committed
        /// </summary>
        /// <returns></returns>
        IEnumerable<IStorageOperation> OperationsFor<T>();

        /// <summary>
        /// All the storage operations that will be executed for documents of type T when this
        /// session is committed
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        IEnumerable<IStorageOperation> OperationsFor(Type documentType);

    }
}
