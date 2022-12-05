using System;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq;

namespace Marten.Services.BatchQuerying;

internal class BatchedOrderedQueryable<T>: BatchedQueryable<T>, IBatchedOrderedQueryable<T> where T : class
{
    public BatchedOrderedQueryable(BatchedQuery parent, IMartenQueryable<T> inner): base(parent, inner)
    {
    }

    public IBatchedOrderedQueryable<T> ThenBy<TKey>(Expression<Func<T, TKey>> expression)
    {
        Inner = ((IOrderedQueryable<T>)Inner).ThenBy(expression).As<IMartenQueryable<T>>();
        return this;
    }

    public IBatchedOrderedQueryable<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> expression)
    {
        Inner = ((IOrderedQueryable<T>)Inner).ThenByDescending(expression).As<IMartenQueryable<T>>();
        return this;
    }
}
