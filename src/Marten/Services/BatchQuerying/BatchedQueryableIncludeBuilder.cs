#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Marten.Services.BatchQuerying;

internal class BatchQueryableIncludeBuilder<T, TInclude> :
    IBatchedQueryableIncludeBuilder<T, TInclude>
    where T : class
    where TInclude : notnull
{
    private readonly BatchedQueryable<T> _batchedQueryable;
    private readonly Action<TInclude> _callback;

    public BatchQueryableIncludeBuilder(BatchedQueryable<T> batchedQueryable, Action<TInclude> callback)
    {
        _batchedQueryable = batchedQueryable;
        _callback = callback;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, object>> idSource)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_callback).On(idSource);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_callback).On(idSource, filter);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On<TId>(Expression<Func<T, TId?>> idSource, Expression<Func<TInclude, TId?>> idMapping)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_callback).On(idSource, idMapping);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_callback).On(idSource, idMapping, filter);
        return _batchedQueryable;
    }
}

internal class BatchedQueryableIncludeDictionaryBuilder<T, TKey, TInclude>
    : IBatchedQueryableIncludeBuilder<T, TKey, TInclude>
    where T : class
    where TInclude : notnull
    where TKey : notnull
{
    private readonly BatchedQueryable<T> _batchedQueryable;
    private readonly IDictionary<TKey, TInclude> _dictionary;

    public BatchedQueryableIncludeDictionaryBuilder(
        BatchedQueryable<T> batchedQueryable,
        IDictionary<TKey, TInclude> dictionary)
    {
        _batchedQueryable = batchedQueryable;
        _dictionary = dictionary;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, object>> idSource)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, filter);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, TKey?>> idSource, Expression<Func<TInclude, TKey?>> idMapping)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On(
        Expression<Func<T, TKey?>> idSource,
        Expression<Func<TInclude, TKey?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping, filter);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping)
        where TId : struct, TKey
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
        where TId : struct, TKey
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping, filter);
        return _batchedQueryable;
    }
}


internal class BatchedQueryableIncludeDictionaryListBuilder<T, TKey, TInclude>
    : IBatchedQueryableIncludeBuilder<T, TKey, TInclude>
    where T : class
    where TInclude : notnull
    where TKey : notnull
{
    private readonly BatchedQueryable<T> _batchedQueryable;
    private readonly IDictionary<TKey, IList<TInclude>> _dictionary;

    public BatchedQueryableIncludeDictionaryListBuilder(
        BatchedQueryable<T> batchedQueryable,
        IDictionary<TKey, IList<TInclude>> dictionary)
    {
        _batchedQueryable = batchedQueryable;
        _dictionary = dictionary;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, object>> idSource)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, filter);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On(Expression<Func<T, TKey?>> idSource, Expression<Func<TInclude, TKey?>> idMapping)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On(
        Expression<Func<T, TKey?>> idSource,
        Expression<Func<TInclude, TKey?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping, filter);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping)
        where TId : struct, TKey
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping);
        return _batchedQueryable;
    }

    public IBatchedQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
        where TId : struct, TKey
    {
        _batchedQueryable.Inner = _batchedQueryable.Inner.Include(_dictionary).On(idSource, idMapping, filter);
        return _batchedQueryable;
    }
}
