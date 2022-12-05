#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Linq;

namespace Marten.Services.BatchQuerying;

internal class BatchedQueryable<T>: IBatchedQueryable<T> where T : class
{
    private readonly BatchedQuery _parent;

    public BatchedQueryable(BatchedQuery parent, IMartenQueryable<T> inner)
    {
        _parent = parent;
        Inner = inner;
    }

    protected IMartenQueryable<T> Inner { get; set; }

    public IBatchedQueryable<T> Stats(out QueryStatistics stats)
    {
        Inner = Inner.Stats(out stats);
        return this;
    }

    public IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate)
    {
        Inner = Inner.Where(predicate).As<IMartenQueryable<T>>();
        return this;
    }

    public IBatchedQueryable<T> Skip(int count)
    {
        Inner = Inner.Skip(count).As<IMartenQueryable<T>>();
        return this;
    }

    public IBatchedQueryable<T> Take(int count)
    {
        Inner = Inner.Take(count).As<IMartenQueryable<T>>();
        return this;
    }

    public IBatchedOrderedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression)
    {
        return new BatchedOrderedQueryable<T>(_parent, Inner.OrderBy(expression).As<IMartenQueryable<T>>());
    }

    public IBatchedOrderedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression)
    {
        return new BatchedOrderedQueryable<T>(_parent, Inner.OrderByDescending(expression).As<IMartenQueryable<T>>());
    }

    public ITransformedBatchQueryable<TValue> Select<TValue>(Expression<Func<T, TValue>> selection)
    {
        return new TransformedBatchQueryable<TValue>(_parent, Inner.Select(selection).As<IMartenQueryable<TValue>>());
    }

    public IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback)
        where TInclude : class
    {
        Inner = Inner.Include(idSource, callback);
        return this;
    }

    public IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list)
        where TInclude : class
    {
        Inner = Inner.Include(idSource, list);
        return this;
    }

    public IBatchedQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
        IDictionary<TKey, TInclude> dictionary) where TKey : notnull where TInclude : class
    {
        Inner = Inner.Include(idSource, dictionary);
        return this;
    }

    public Task<long> Count()
    {
        return _parent.Count(Inner);
    }

    public Task<long> Count(Expression<Func<T, bool>> filter)
    {
        var queryable = Inner.Where(filter).As<IMartenQueryable<T>>();
        return _parent.Count(queryable);
    }

    public Task<bool> Any()
    {
        return _parent.Any(Inner);
    }

    public Task<bool> Any(Expression<Func<T, bool>> filter)
    {
        return _parent.Any(Inner.Where(filter).As<IMartenQueryable<T>>());
    }

    public Task<IReadOnlyList<T>> ToList()
    {
        return _parent.Query(Inner);
    }

    public Task<T> First()
    {
        return _parent.First(Inner);
    }

    public Task<T> First(Expression<Func<T, bool>> filter)
    {
        return _parent.First(Inner.Where(filter).As<IMartenQueryable<T>>());
    }

    public Task<T?> FirstOrDefault()
    {
        return _parent.FirstOrDefault(Inner);
    }

    public Task<T?> FirstOrDefault(Expression<Func<T, bool>> filter)
    {
        return _parent.FirstOrDefault(Inner.Where(filter).As<IMartenQueryable<T>>());
    }

    public Task<T> Single()
    {
        return _parent.Single(Inner);
    }

    public Task<T> Single(Expression<Func<T, bool>> filter)
    {
        return _parent.Single(Inner.Where(filter).As<IMartenQueryable<T>>());
    }

    public Task<T?> SingleOrDefault()
    {
        return _parent.SingleOrDefault(Inner);
    }

    public Task<T?> SingleOrDefault(Expression<Func<T, bool>> filter)
    {
        return _parent.SingleOrDefault(Inner.Where(filter).As<IMartenQueryable<T>>());
    }

    public Task<TResult> Min<TResult>(Expression<Func<T, TResult>> expression)
    {
        return _parent.Min(Inner.Select(expression));
    }

    public Task<TResult> Max<TResult>(Expression<Func<T, TResult>> expression)
    {
        return _parent.Max(Inner.Select(expression));
    }

    public Task<TResult> Sum<TResult>(Expression<Func<T, TResult>> expression)
    {
        return _parent.Sum(Inner.Select(expression));
    }

    public Task<double> Average(Expression<Func<T, object>> expression)
    {
        return _parent.Average(Inner.Select(expression));
    }
}
