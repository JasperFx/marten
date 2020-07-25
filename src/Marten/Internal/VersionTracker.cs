using System;
using System.Collections.Generic;
using Marten.Exceptions;

namespace Marten.Internal
{
    public class VersionTracker
    {
        private readonly Dictionary<Type, object> _byType
            = new Dictionary<Type, object>();

        public Dictionary<TId, Guid> ForType<TDoc, TId>()
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

        public Guid? VersionFor<TDoc, TId>(TId id)
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

        public void StoreVersion<TDoc, TId>(TId id, Guid guid)
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
                var dict = new Dictionary<TId, Guid> {[id] = guid};
                _byType.Add(typeof(TDoc), dict);
            }
        }

        public void ClearVersion<TDoc, TId>(TId id)
        {
            if (_byType.TryGetValue(typeof(TDoc), out var item))
            {
                if (item is Dictionary<TId, Guid> dict)
                {
                    dict.Remove(id);
                }
            }
        }
    }
}
