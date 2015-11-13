using System;
using System.Collections.Generic;
using System.Data;
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

        T Load<T>(string id) where T : class;
        T Load<T>(ValueType id) where T : class;

        ILoadByKeys<T> Load<T>(); 

        // Store by etag? Version strategy?

        void Store<T>(T entity) where T : class;

        IQueryable<T> Query<T>();

        IEnumerable<T> Query<T>(string sql, params object[] parameters);

        void BulkInsert<T>(T[] documents, int batchSize = 1000);

        IDiagnostics Diagnostics { get; }
    }

    public interface ILoadByKeys<out TDoc>
    {
        IEnumerable<TDoc> ById<TKey>(params TKey[] keys);
        IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys);
    }

    public interface IDiagnostics
    {
        IDbCommand CommandFor<T>(IQueryable<T> queryable);
    }

    
}