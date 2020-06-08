using System;
using System.Collections.Generic;
using Marten.Schema;

namespace Marten.V4Internals
{
    public abstract class IdentityMapDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public IdentityMapDocumentStorage(IQueryableDocument document) : base(document)
        {
        }

        public sealed override void Eject(IMartenSession session, T document)
        {
            var id = Identity(document);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d.Remove(id);
                }
            }
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            var id = Identity(document);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d[id] = document;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }
            else
            {
                var dict = new Dictionary<TId, T> {{id, document}};
                session.ItemMap.Add(typeof(T), dict);
            }
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            var id = Identity(document);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d[id] = document;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }
            else
            {
                var dict = new Dictionary<TId, T> {{id, document}};
                session.ItemMap.Add(typeof(T), dict);
            }

            if (version != null)
            {
                session.Versions.StoreVersion<T, TId>(id, version.Value);
            }
            else
            {
                session.Versions.ClearVersion<T, TId>(id);
            }
        }
    }
}
