using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQueryable<T>: IBatchedFetcher<T>
    {
        IBatchedQueryable<T> Stats(out QueryStatistics stats);

        IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate);

        IBatchedQueryable<T> Skip(int count);

        IBatchedQueryable<T> Take(int count);

        IBatchedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression);

        IBatchedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression);

        ITransformedBatchQueryable<TValue> Select<TValue>(Expression<Func<T, TValue>> selection);

        IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback) where TInclude : class;

        IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list) where TInclude : class;

        IBatchedQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary) where TInclude : class;

        Task<TResult> Min<TResult>(Expression<Func<T, TResult>> expression);

        Task<TResult> Max<TResult>(Expression<Func<T, TResult>> expression);

        Task<TResult> Sum<TResult>(Expression<Func<T, TResult>> expression);

        Task<double> Average(Expression<Func<T, object>> expression);
    }
}
