using System;
using System.Collections.Generic;

namespace Marten.V4Internals
{
    public class VersionTracker
    {
        private readonly Dictionary<Type, object> _byType
            = new Dictionary<Type, object>();

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
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(TDoc)}");
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
