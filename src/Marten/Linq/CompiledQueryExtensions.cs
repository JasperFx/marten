using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq
{
    public static class CompiledQueryExtensions
    {
        /// <summary>
        /// A query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
        /// </summary>
        /// <typeparam name="TDoc">The document</typeparam>
        /// <typeparam name="TOut">The output</typeparam>
        /// <param name="session">The session</param>
        /// <param name="query">The instance of a compiled query</param>
        /// <returns>A single item query result</returns>
        public static TOut Query<TDoc, TOut>(this IQuerySession session, ICompiledQuery<TDoc, TOut> query)
        {
            return session.DocumentStore.CompiledQueryExecutor.ExecuteQuery(session, query);
        }

        /// <summary>
        /// An async query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
        /// </summary>
        /// <typeparam name="TDoc">The document</typeparam>
        /// <typeparam name="TOut">The output</typeparam>
        /// <param name="session">The session</param>
        /// <param name="query">The instance of a compiled query</param>
        /// <param name="token">A cancellation token</param>
        /// <returns>A task for a single item query result</returns>
        public static Task<TOut> QueryAsync<TDoc, TOut>(this IQuerySession session, ICompiledQuery<TDoc, TOut> query, CancellationToken token = default(CancellationToken))
        {
            return session.DocumentStore.CompiledQueryExecutor.ExecuteQueryAsync(session, query, token);
        }

        /// <summary>
        /// A query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
        /// </summary>
        /// <typeparam name="TDoc">The document</typeparam>
        /// <typeparam name="TOut">The output</typeparam>
        /// <param name="session">The session</param>
        /// <param name="query">The instance of a compiled query</param>
        /// <returns>An enumerable query result</returns>
        public static IEnumerable<TOut> Query<TDoc, TOut>(this IQuerySession session, IEnumerableCompiledQuery<TDoc, TOut> query)
        {
            return session.DocumentStore.CompiledQueryExecutor.ExecuteQuery(session, query);
        }

        /// <summary>
        /// An async query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
        /// </summary>
        /// <typeparam name="TDoc">The document</typeparam>
        /// <typeparam name="TOut">The output</typeparam>
        /// <param name="session">The session</param>
        /// <param name="query">The instance of a compiled query</param>
        /// <param name="token">A cancellation token</param>
        /// <returns>A Task for an enumerable query result</returns>
 
        public static Task<IEnumerable<TOut>> QueryAsync<TDoc, TOut>(this IQuerySession session, IEnumerableCompiledQuery<TDoc, TOut> query, CancellationToken token = default(CancellationToken))
        {
            return session.DocumentStore.CompiledQueryExecutor.ExecuteQueryAsync(session, query, token);
        }
    }
}