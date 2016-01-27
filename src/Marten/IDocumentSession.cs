using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Marten
{
    public interface IQuerySession : IDisposable
    {
        T Load<T>(string id) where T : class;
        Task<T> LoadAsync<T>(string id, CancellationToken token = default(CancellationToken)) where T : class;
        T Load<T>(ValueType id) where T : class;
        Task<T> LoadAsync<T>(ValueType id, CancellationToken token = default(CancellationToken)) where T : class;

        ILoadByKeys<T> Load<T>() where T : class;

        string FindJsonById<T>(string id) where T : class;
        string FindJsonById<T>(ValueType id) where T : class;
        Task<string> FindJsonByIdAsync<T>(string id, CancellationToken token = default(CancellationToken)) where T : class;
        Task<string> FindJsonByIdAsync<T>(ValueType id, CancellationToken token = default(CancellationToken)) where T : class;

        // SAMPLE: querying_with_linq
        /// <summary>
        /// Use Linq operators to query the documents
        /// stored in Postgresql
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IQueryable<T> Query<T>();
        // ENDSAMPLE

        IEnumerable<T> Query<T>(string sql, params object[] parameters);

        Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken token = default(CancellationToken), params object[] parameters);
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
        Task SaveChangesAsync(CancellationToken token = default(CancellationToken));

        // Store by etag? Version strategy?

        void Store<T>(T entity) where T : class;
    }

    public interface ILoadByKeys<TDoc>
    {
        IEnumerable<TDoc> ById<TKey>(params TKey[] keys);
        Task<IEnumerable<TDoc>> ByIdAsync<TKey>(params TKey[] keys);
        IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys);
        Task<IEnumerable<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys, CancellationToken token = default(CancellationToken));
    }

    public interface IDiagnostics
    {
        IDbCommand CommandFor<T>(IQueryable<T> queryable);
        string DocumentStorageCodeFor<T>();
    }
}