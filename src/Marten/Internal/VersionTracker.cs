using System;
using System.Collections.Generic;
using Marten.Exceptions;

namespace Marten.Internal;

public class VersionTracker: IVersionTracker
{
    private readonly Dictionary<Type, object> _byType = new();

    public Dictionary<TId, long> RevisionsFor<TDoc, TId>() where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, long> d)
            {
                return d;
            }

            throw new DocumentIdTypeMismatchException(typeof(TDoc), typeof(TId));
        }

        var dict = new Dictionary<TId, long>();
        _byType[typeof(TDoc)] = dict;

        return dict;
    }

    public Dictionary<TId, Guid> ForType<TDoc, TId>() where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, Guid> d)
            {
                return d;
            }

            throw new DocumentIdTypeMismatchException(typeof(TDoc), typeof(TId));
        }

        var dict = new Dictionary<TId, Guid>();
        _byType[typeof(TDoc)] = dict;

        return dict;
    }

    public Guid? VersionFor<TDoc, TId>(TId id) where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, Guid> dict)
            {
                if (dict.TryGetValue(id, out var version))
                {
                    return version;
                }
            }

            return null;
        }

        return null;
    }

    public long? RevisionFor<TDoc, TId>(TId id) where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, long> dict)
            {
                if (dict.TryGetValue(id, out var version))
                {
                    return version;
                }
            }

            return null;
        }

        return null;
    }

    public void StoreVersion<TDoc, TId>(TId id, Guid guid) where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, Guid> d)
            {
                d[id] = guid;
            }
            else
            {
                throw new DocumentIdTypeMismatchException(typeof(TDoc), typeof(TId));
            }
        }
        else
        {
            var dict = new Dictionary<TId, Guid> { [id] = guid };
            _byType.Add(typeof(TDoc), dict);
        }
    }

    public void StoreRevision<TDoc, TId>(TId id, long revision) where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, long> d)
            {
                d[id] = revision;
            }
            else
            {
                throw new DocumentIdTypeMismatchException(typeof(TDoc), typeof(TId));
            }
        }
        else
        {
            var dict = new Dictionary<TId, long> { [id] = revision };
            _byType.Add(typeof(TDoc), dict);
        }
    }

    public void ClearVersion<TDoc, TId>(TId id) where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, Guid> dict)
            {
                dict.Remove(id);
            }
        }
    }

    public void ClearRevision<TDoc, TId>(TId id) where TId : notnull
    {
        if (_byType.TryGetValue(typeof(TDoc), out var item))
        {
            if (item is Dictionary<TId, long> dict)
            {
                dict.Remove(id);
            }
        }
    }
}
