#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Internal.Storage;
using Marten.Linq.Parsing;

namespace Marten.Linq.Includes;

internal class MartenQueryableIncludeBuilder<T, TInclude>: IMartenQueryableIncludeBuilder<T, TInclude>
    where TInclude : notnull
{
    private readonly MartenLinqQueryable<T> _martenLinqQueryable;
    private readonly Action<TInclude> _callback;

    public MartenQueryableIncludeBuilder(MartenLinqQueryable<T> martenLinqQueryable, Action<TInclude> callback)
    {
        _martenLinqQueryable = martenLinqQueryable;
        _callback = callback;
    }

    public IMartenQueryable<T> On(Expression<Func<T, object>> idSource)
    {
        var include = BuildInclude(idSource);
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter)
    {
        var include = BuildInclude(idSource);
        include.Where = filter;
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On<TId>(Expression<Func<T, TId?>> idSource, Expression<Func<TInclude, TId?>> idMapping)
    {
        var include = BuildInclude(idSource, idMapping);
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
    {
        var include = BuildInclude(idSource, idMapping);
        include.Where = filter;
        return _martenLinqQueryable.IncludePlan(include);
    }

    internal IIncludePlan BuildInclude(Expression<Func<T, object>> idSource, Expression? where = null)
    {
        var storage = (IDocumentStorage<TInclude>)_martenLinqQueryable.Session.StorageFor(typeof(TInclude));
        var identityMember = _martenLinqQueryable.Session.StorageFor(typeof(T)).QueryMembers.MemberFor(idSource);

        var include = new IncludePlan<TInclude>(storage, identityMember, _callback) { Where = where };

        return include;
    }

    internal IIncludePlan BuildInclude<TId>(
        Expression<Func<T, TId>> idSource,
        Expression<Func<TInclude, TId>> idMapping,
        Expression? where = null)
    {
        var storage = (IDocumentStorage<TInclude>)_martenLinqQueryable.Session.StorageFor(typeof(TInclude));
        var identityMember = _martenLinqQueryable.Session.StorageFor(typeof(T)).QueryMembers.MemberFor(idSource);
        var mappingMember = _martenLinqQueryable.Session.StorageFor(typeof(TInclude)).QueryMembers.MemberFor(idMapping);

        var include = new IncludePlan<TInclude>(storage, identityMember, mappingMember, _callback) { Where = where };

        return include;
    }
}

internal class MartenQueryableIncludeBuilder<T, TKey, TInclude>
    : IMartenQueryableIncludeBuilder<T, TKey, TInclude>
    where TInclude : notnull where TKey : notnull
{
    private readonly MartenLinqQueryable<T> _martenLinqQueryable;
    private readonly Action<TKey, TInclude> _dictionaryCallback;

    public MartenQueryableIncludeBuilder(
        MartenLinqQueryable<T> martenLinqQueryable,
        IDictionary<TKey, TInclude> dictionary)
    {
        _martenLinqQueryable = martenLinqQueryable;
        _dictionaryCallback = (id, include) => dictionary[id] = include;
    }

    public MartenQueryableIncludeBuilder(
        MartenLinqQueryable<T> martenLinqQueryable,
        IDictionary<TKey, IList<TInclude>> dictionary)
    {
        _martenLinqQueryable = martenLinqQueryable;
        _dictionaryCallback = (id, include) =>
        {
            if (!dictionary.TryGetValue(id, out var list))
            {
                list = new List<TInclude>();
                dictionary[id] = list;
            }

            list.Add(include);
        };
    }

    public IMartenQueryable<T> On(Expression<Func<T, object>> idSource)
    {
        var include = BuildInclude(idSource);
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter)
    {
        var include = BuildInclude(idSource);
        include.Where = filter;
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On(Expression<Func<T, TKey?>> idSource, Expression<Func<TInclude, TKey?>> idMapping)
    {
        var include = BuildInclude(idSource, idMapping);
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On(
        Expression<Func<T, TKey?>> idSource,
        Expression<Func<TInclude, TKey?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
    {
        var include = BuildInclude(idSource, idMapping);
        include.Where = filter;
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping)
        where TId : struct, TKey
    {
        var include = BuildInclude(idSource, idMapping);
        return _martenLinqQueryable.IncludePlan(include);
    }

    public IMartenQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
        where TId : struct, TKey
    {
        var include = BuildInclude(idSource, idMapping);
        include.Where = filter;
        return _martenLinqQueryable.IncludePlan(include);
    }

    internal IIncludePlan BuildInclude(Expression<Func<T, object>> idSource)
    {
        var storage = (IDocumentStorage<TInclude>)_martenLinqQueryable.Session.StorageFor(typeof(TInclude));
        if (storage is IDocumentStorage<TInclude, TKey> typedStorage)
        {
            var identityMember = _martenLinqQueryable.Session.StorageFor(typeof(T)).QueryMembers.MemberFor(idSource);

            void Callback(TInclude item)
            {
                var id = typedStorage.Identity(item);
                _dictionaryCallback(id, item);
            }

            return new IncludePlan<TInclude>(storage, identityMember, Callback);
        }

        throw new DocumentIdTypeMismatchException(storage, typeof(TKey));
    }

    internal IIncludePlan BuildInclude(Expression<Func<T, TKey?>> idSource, Expression<Func<TInclude, TKey?>> idMapping)
    {
        var mapFunc = idMapping.Compile();

        void Callback(TInclude item)
        {
            var id = mapFunc(item);
            if (id is not null)
            {
                _dictionaryCallback(id, item);
            }
        }

        var storage = (IDocumentStorage<TInclude>)_martenLinqQueryable.Session.StorageFor(typeof(TInclude));

        var identityMember = _martenLinqQueryable.Session.StorageFor(typeof(T)).QueryMembers.MemberFor(idSource);
        var mappingMember = _martenLinqQueryable.Session.StorageFor(typeof(TInclude)).QueryMembers.MemberFor(idMapping);

        return new IncludePlan<TInclude>(storage, identityMember, mappingMember, Callback);
    }

    internal IIncludePlan BuildInclude<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping)
        where TId : struct, TKey
    {
        var mapFunc = idMapping.Compile();

        void Callback(TInclude item)
        {
            var id = mapFunc(item);
            if (id.HasValue)
            {
                _dictionaryCallback(id.Value, item);
            }
        }

        var storage = (IDocumentStorage<TInclude>)_martenLinqQueryable.Session.StorageFor(typeof(TInclude));

        var identityMember = _martenLinqQueryable.Session.StorageFor(typeof(T)).QueryMembers.MemberFor(idSource);
        var mappingMember = _martenLinqQueryable.Session.StorageFor(typeof(TInclude)).QueryMembers.MemberFor(idMapping);

        return new IncludePlan<TInclude>(storage, identityMember, mappingMember, Callback);
    }
}
