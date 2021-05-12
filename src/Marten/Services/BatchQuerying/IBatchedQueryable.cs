using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
#nullable enable
namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQueryable<T>: IBatchedFetcher<T>
    {
        /// <summary>
        /// Retrieve the total number of persisted rows in the database that match this
        /// query. Useful for server side paging.
        /// </summary>
        /// <param name="stats"></param>
        /// <returns></returns>
        IBatchedQueryable<T> Stats(out QueryStatistics stats);

        IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate);

        IBatchedQueryable<T> Skip(int count);

        IBatchedQueryable<T> Take(int count);

        IBatchedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression);

        IBatchedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression);

        ITransformedBatchQueryable<TValue> Select<TValue>(Expression<Func<T, TValue>> selection);

        /// <summary>
        /// Also fetch related documents, and call the callback lambda for each
        /// related document
        /// </summary>
        /// <param name="idSource"></param>
        /// <param name="callback"></param>
        /// <typeparam name="TInclude"></typeparam>
        /// <returns></returns>
        IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback) where TInclude : class;

        /// <summary>
        /// Also fetch related documents, and add the related documents to
        /// the supplied list
        /// </summary>
        /// <param name="idSource"></param>
        /// <param name="list"></param>
        /// <typeparam name="TInclude"></typeparam>
        /// <returns></returns>
        IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list) where TInclude : class;

        /// <summary>
        /// Also fetch related documents, and add the related documents to
        /// the supplied dictionary organized by the identity of the related document
        /// </summary>
        /// <param name="idSource"></param>
        /// <param name="dictionary"></param>
        /// <typeparam name="TInclude"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <returns></returns>
        IBatchedQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary) where TInclude : class where TKey: notnull;

        Task<TResult> Min<TResult>(Expression<Func<T, TResult>> expression);

        Task<TResult> Max<TResult>(Expression<Func<T, TResult>> expression);

        Task<TResult> Sum<TResult>(Expression<Func<T, TResult>> expression);

        Task<double> Average(Expression<Func<T, object>> expression);
    }
}
