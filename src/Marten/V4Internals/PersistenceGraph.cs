using System;
using Baseline;
using LamarCodeGeneration;
using Marten.Schema;
using Marten.Util;

namespace Marten.V4Internals
{
    public class PersistenceGraph: IPersistenceGraph
    {
        private readonly StoreOptions _options;
        private ImHashMap<Type, object> _storage = ImHashMap<Type, object>.Empty;

        public PersistenceGraph(StoreOptions options)
        {
            _options = options;
        }

        public DocumentPersistence<T> StorageFor<T>()
        {
            if (_storage.TryFind(typeof(T), out var stored))
            {
                return stored.As<DocumentPersistence<T>>();
            }

            var mapping = _options.Storage.FindMapping(typeof(T));

            if (mapping is DocumentMapping m)
            {
                var builder = new DocumentPersistenceBuilder(m, _options);
                var slot = builder.Generate<T>();

                _storage = _storage.AddOrUpdate(typeof(T), slot);

                return slot;
            }

            if (mapping is SubClassMapping s)
            {
                var loader =
                    typeof(SubClassLoader<,,>).CloseAndBuildAs<ISubClassLoader<T>>(mapping.Root.DocumentType, typeof(T),
                        mapping.IdType);

                var slot = loader.BuildPersistence(this, s);
                _storage = _storage.AddOrUpdate(typeof(T), slot);

                return slot;
            }

            throw new NotSupportedException("Unable to build document persistence handlers for " + mapping.DocumentType.FullNameInCode());

        }

        private interface ISubClassLoader<T>
        {
            DocumentPersistence<T> BuildPersistence(IPersistenceGraph graph, SubClassMapping mapping);
        }

        private class SubClassLoader<TRoot, T, TId> : ISubClassLoader<T> where T : TRoot
        {
            public DocumentPersistence<T> BuildPersistence(IPersistenceGraph graph, SubClassMapping mapping)
            {
                var inner = graph.StorageFor<TRoot>();

                return new DocumentPersistence<T>()
                {
                    QueryOnly = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.QueryOnly, mapping),
                    Lightweight = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.Lightweight, mapping),
                    IdentityMap = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.IdentityMap, mapping),
                    DirtyTracking = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.DirtyTracking, mapping),
                    BulkLoader = new SubClassBulkLoader<T, TRoot>(inner.BulkLoader)
                };
            }
        }
    }
}
