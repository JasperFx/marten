using System;
using System.Linq.Expressions;

namespace Marten.Services.BatchQuerying;

public interface IBatchedOrderedQueryable<T>: IBatchedQueryable<T>
{
    IBatchedOrderedQueryable<T> ThenBy<TKey>(Expression<Func<T, TKey>> expression);

    IBatchedOrderedQueryable<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> expression);
}
