using System;
using System.Collections.Generic;

namespace Marten.Services
{
    public interface IUnitOfWork
    {
        /// <summary>
        /// All of the pending deletions that will be processed
        /// when this session is committed
        /// </summary>
        /// <returns></returns>
        IEnumerable<Delete> Deletions();

        /// <summary>
        /// All the pending deletions of documents of type T that will be processed
        /// when this session is committed
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<Delete> DeletionsFor<T>();

        /// <summary>
        /// All the pending deletions of documents of type documentType that will be processed
        /// when this session is committed
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        IEnumerable<Delete> DeletionsFor(Type documentType);

        /// <summary>
        /// All the documents that will be updated when this session is committed
        /// </summary>
        /// <returns></returns>
        IEnumerable<object> Updates();

        /// <summary>
        /// All of the documents that will be inserted when this session is committed
        /// </summary>
        /// <returns></returns>
        IEnumerable<object> Inserts();

        /// <summary>
        /// All the documents of type T that will be updated when this session is committed
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
    }
}