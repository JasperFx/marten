using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten
{
    /// <summary>
    ///     Interface for document session
    /// </summary>
    public interface IDocumentSession : IDisposable
    {
        void Delete<T>(T entity);
        void Delete<T>(ValueType id);
        void Delete<T>(string id);

        /// <summary>
        ///     Saves all the pending changes to the server.
        /// </summary>
        void SaveChanges();

        T Load<T>(string id);
        T Load<T>(ValueType id);

        ILoadByKeys<T> Load<T>(); 

        // Store by etag? Version strategy?

        /// <summary>
        ///     Stores entity in session, extracts Id from entity using Conventions or generates new one if it is not available.
        ///     <para>Forces concurrency check if the Id is not available during extraction.</para>
        /// </summary>
        /// <param name="entity">entity to store.</param>
        void Store(object entity);

        IQueryable<T> Query<T>();

        IEnumerable<T> Query<T>(string @where, params object[] parameters);
    }

    public interface ILoadByKeys<out TDoc>
    {
        IEnumerable<TDoc> ById<TKey>(params TKey[] keys);
        IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys);
    }
}