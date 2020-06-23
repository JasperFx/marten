using System;
using System.Collections.Generic;
using Marten.Schema;

namespace Marten.V4Internals
{
    // TODO -- this isn't real yet
    public abstract class DocumentSelectorWithDirtyChecking<T, TId>
    {
        protected readonly DocumentMapping _mapping;
        protected readonly ISerializer _serializer;
        protected readonly Dictionary<TId, T> _identityMap;
        protected readonly Dictionary<TId, Guid> _versions;

        public DocumentSelectorWithDirtyChecking(IMartenSession session, DocumentMapping mapping)
        {
            _mapping = mapping;
            _serializer = session.Serializer;
            _versions = session.Versions.ForType<T, TId>();
            if (session.ItemMap.TryGetValue(typeof(T), out var dict))
            {
                _identityMap = (Dictionary<TId, T>)dict;
            }
            else
            {
                _identityMap = new Dictionary<TId, T>();
                session.ItemMap[typeof(T)] = _identityMap;
            }
        }
    }
}
