using System;
using System.Runtime.CompilerServices;
using Baseline;
using Baseline.ImTools;
using LamarCodeGeneration;
using LamarCompiler;
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
            _storage = _storage.AddOrUpdate(typeof(T), provider);
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
                var rules = _options.CreateGenerationRules();
                _options.EventGraph.InitializeSynchronously(rules, _options.EventGraph, null);

                _storage = _storage.AddOrUpdate(documentType, _options.EventGraph.Provider);

                return _options.EventGraph.Provider.As<DocumentProvider<T>>();
            }

            var mapping = _options.Storage.FindMapping(documentType);

            switch (mapping)
            {
                case DocumentMapping m:
                {
                    try
                    {
                        var builder = new DocumentProviderBuilder(m, _options);

                        var rules = _options.CreateGenerationRules();
                        rules.ReferenceTypes(m.DocumentType);
                        builder.InitializeSynchronously(rules, _options, null);
                        var slot = builder.BuildProvider<T>();

                        _storage = _storage.AddOrUpdate(documentType, slot);

                        return slot;
                    }
                    catch (Exception e)
                    {
                        if (e.Message.Contains("is inaccessible due to its protection level"))
                        {
                            throw new InvalidOperationException($"Requested document type '{mapping.DocumentType.FullNameInCode()}' must be scoped as 'public' in order to be used as a document type inside of Marten", e);
                        }

                        throw;

                    }
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
                    var slot = new DocumentProvider<T>(null, storage, storage, storage, storage);
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

                var queryOnly = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.QueryOnly, mapping);
                var lightweight = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.Lightweight, mapping);
                var identityMap = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.IdentityMap, mapping);
                var dirtyTracking = new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>) inner.DirtyTracking, mapping);

                var bulkLoader = new SubClassBulkLoader<T, TRoot>(inner.BulkLoader);
                return new DocumentProvider<T>(bulkLoader, queryOnly, lightweight, identityMap, dirtyTracking);
            }
        }
    }
}
