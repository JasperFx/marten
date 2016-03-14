using System;
using System.Linq.Expressions;

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


    }
}