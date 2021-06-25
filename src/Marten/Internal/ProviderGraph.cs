using System;
using Baseline;
using Baseline.ImTools;
using LamarCodeGeneration;
using Marten.Events;
using Marten.Events.CodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq.Clauses;

#nullable enable
namespace Marten.Internal
{
    public class ProviderGraph: IProviderGraph
    {
        private readonly StoreOptions _options;
        private ImHashMap<Type, object> _storage = ImHashMap<Type, object>.Empty;

        public ProviderGraph(StoreOptions options)
        {
            _options = options;
        }

        public void Append<T>(DocumentProvider<T> provider)
        {
            _storage = _storage.Update(typeof(T), provider);
        }

        public DocumentProvider<T> StorageFor<T>() where T : notnull
        {
            var documentType = typeof(T);

            if (_storage.TryFind(documentType, out var stored))
            {
                return stored.As<DocumentProvider<T>>();
            }

            if (documentType == typeof(IEvent))
            {
                var slot = EventDocumentStorageGenerator.BuildProvider(_options);

                _storage = _storage.AddOrUpdate(documentType, slot);

                return slot.As<DocumentProvider<T>>();
            }

            var mapping = _options.Storage.FindMapping(documentType);

            switch (mapping)
            {
                case DocumentMapping m:
                {
                    var builder = new DocumentPersistenceBuilder(m, _options);
                    var slot = builder.Generate<T>();

                    _storage = _storage.AddOrUpdate(documentType, slot);

                    return slot;
                }
                case SubClassMapping s:
                {
                    var loader =
                        typeof(SubClassLoader<,,>).CloseAndBuildAs<ISubClassLoader<T>>(mapping.Root.DocumentType, documentType,
                            mapping.IdType);

                    var slot = loader.BuildPersistence(this, s);
                    _storage = _storage.AddOrUpdate(documentType, slot);

                    return slot;
                }
                case EventMapping em:
                {
                    var storage = (IDocumentStorage<T>) em;
                    var slot = new DocumentProvider<T> {Lightweight = storage, IdentityMap = storage, DirtyTracking = storage, QueryOnly = storage};
                    _storage = _storage.AddOrUpdate(documentType, slot);

                    return slot;
                }
                default:
                    throw new NotSupportedException("Unable to build document persistence handlers for " + mapping.DocumentType.FullNameInCode());
            }
        }

        private interface ISubClassLoader<T>
        {
            DocumentProvider<T> BuildPersistence(IProviderGraph graph, SubClassMapping mapping);
        }

        private class SubClassLoader<TRoot, T, TId>: ISubClassLoader<T> where T : TRoot where TId : notnull where TRoot: notnull
        {
            public DocumentProvider<T> BuildPersistence(IProviderGraph graph, SubClassMapping mapping)
            {
                var inner = graph.StorageFor<TRoot>();

                return new DocumentProvider<T>()
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
