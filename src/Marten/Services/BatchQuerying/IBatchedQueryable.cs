using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Services.Includes;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQueryable<T> : IBatchedFetcher<T>
    {
        IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate);
        IBatchedQueryable<T> Skip(int count);
        IBatchedQueryable<T> Take(int count);
        IBatchedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression);
        IBatchedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression);

        ITransformedBatchQueryable<TValue> Select<TValue>(Expression<Func<T, TValue>> selection);

        IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback, JoinType joinType = JoinType.Inner) where TInclude : class;
        IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list, JoinType joinType = JoinType.Inner) where TInclude : class;
        IBatchedQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource, IDictionary<TKey, TInclude> dictionary, JoinType joinType = JoinType.Inner) where TInclude : class;
    }
}