using System;
using System.Collections.Generic;
using Marten.Internal.DirtyTracking;
using Marten.Schema;

namespace Marten.Internal.CodeGeneration
{
    public abstract class DocumentSelectorWithDirtyChecking<T, TId> : IDocumentSelector
    {
        protected readonly DocumentMapping _mapping;
        protected readonly ISerializer _serializer;
        protected readonly Dictionary<TId, T> _identityMap;
        protected readonly Dictionary<TId, Guid> _versions;
        protected readonly IMartenSession _session;

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

            _session = session;
        }

        public void StoreTracker(IMartenSession session, T document)
        {
            var tracker = new ChangeTracker<T>(session, document);
            session.ChangeTrackers.Add(tracker);
        }

    }
}
