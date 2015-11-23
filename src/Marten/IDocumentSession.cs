using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Marten
{

    public interface IQuerySession
    {
        T Load<T>(string id) where T : class;
        T Load<T>(ValueType id) where T : class;

        ILoadByKeys<T> Load<T>() where T : class;


        IQueryable<T> Query<T>();

        IEnumerable<T> Query<T>(string sql, params object[] parameters);

    }

    /// <summary>
    ///     Interface for document session
    /// </summary>
    public interface IDocumentSession : IQuerySession, IDisposable
    {
        void Delete<T>(T entity);
        void Delete<T>(ValueType id);
        void Delete<T>(string id);

        /// <summary>
        ///     Saves all the pending changes to the server.
        /// </summary>
        void SaveChanges();


        // Store by etag? Version strategy?

        void Store<T>(T entity) where T : class;

    }

    public interface ILoadByKeys<out TDoc>
    {
        IEnumerable<TDoc> ById<TKey>(params TKey[] keys);
        IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys);
    }

    public interface IDiagnostics
    {
        IDbCommand CommandFor<T>(IQueryable<T> queryable);
        string DocumentStorageCodeFor<T>();
    }

    
}