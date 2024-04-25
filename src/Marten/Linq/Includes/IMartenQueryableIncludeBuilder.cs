#nullable enable
using System;
using System.Linq.Expressions;

namespace Marten.Linq.Includes;

public interface IMartenQueryableIncludeBuilder<T, TInclude> where TInclude : notnull
{
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource);
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter);
    IMartenQueryable<T> On<TId>(Expression<Func<T, TId?>> idSource, Expression<Func<TInclude, TId?>> idMapping);

    IMartenQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter);
}

public interface IMartenQueryableIncludeBuilder<T, TKey, TInclude> where TInclude : notnull where TKey : notnull
{
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource);
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter);
    IMartenQueryable<T> On(Expression<Func<T, TKey?>> idSource, Expression<Func<TInclude, TKey?>> idMapping);

    IMartenQueryable<T> On(
        Expression<Func<T, TKey?>> idSource,
        Expression<Func<TInclude, TKey?>> idMapping,
        Expression<Func<TInclude, bool>> filter);

    IMartenQueryable<T> On<TId>(Expression<Func<T, TId?>> idSource, Expression<Func<TInclude, TId?>> idMapping)
        where TId : struct, TKey;

    IMartenQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
        where TId : struct, TKey;
}
