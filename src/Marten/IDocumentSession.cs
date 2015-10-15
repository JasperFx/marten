using System;
using System.Collections.Generic;

namespace Marten
{
    /// <summary>
    ///     Interface for document session
    /// </summary>
    public interface IDocumentSession : IDisposable
    {
        void Delete<T>(T entity);
        void Delete<T>(ValueType id);
        void Delete(string id);

        T Load<T>(string id);
        T[] Load<T>(IEnumerable<string> ids);
        T Load<T>(ValueType id);
        T[] Load<T>(params ValueType[] ids);
        T[] Load<T>(IEnumerable<ValueType> ids);

        /// <summary>
        ///     Saves all the pending changes to the server.
        /// </summary>
        void SaveChanges();

        // Store by etag? Version strategy?

        /// <summary>
        ///     Stores entity in session, extracts Id from entity using Conventions or generates new one if it is not available.
        ///     <para>Forces concurrency check if the Id is not available during extraction.</para>
        /// </summary>
        /// <param name="entity">entity to store.</param>
        void Store(object entity);
    }
}