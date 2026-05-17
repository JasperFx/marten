#nullable enable
using System;
using ImTools;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.RuntimeCompiler;
using Marten.Events;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Internal.ClosedShape;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class ProviderGraph: IProviderGraph
{
    private readonly StoreOptions _options;
    private readonly System.Threading.Lock _storageLock = new();
    private ImHashMap<Type, object> _storage = ImHashMap<Type, object>.Empty;

    public ProviderGraph(StoreOptions options)
    {
        _options = options;
    }

    public void Append<T>(DocumentProvider<T> provider) where T : notnull
    {
        lock (_storageLock)
        {
            _storage = _storage.AddOrUpdate(typeof(T), provider);
        }
    }

    public DocumentProvider<T> StorageFor<T>() where T : notnull
    {
        var documentType = typeof(T);

        // 9.0 (#4374): hoist _storage into a local for the unlocked fast path. The
        // JIT can't prove the second read sees the same ImHashMap reference as the
        // first (it isn't volatile), so it re-emits a field load on every TryFind.
        // Reading into a local gives the JIT a stable snapshot to work against and
        // shortens the fast path; the locked branch re-reads _storage on purpose
        // because Append() may have published a newer map.
        var snapshot = _storage;
        if (snapshot.TryFind(documentType, out var stored))
        {
            return stored.As<DocumentProvider<T>>();
        }

        lock (_storageLock)
        {
            if (_storage.TryFind(documentType, out stored))
            {
                return stored.As<DocumentProvider<T>>();
            }

            return CreateDocumentProvider<T>();
        }
    }

    internal DocumentProvider<T> CreateDocumentProvider<T>() where T : notnull
    {
        var documentType = typeof(T);

        if (documentType == typeof(IEvent))
        {
            var rules = _options.CreateGenerationRules();
            // 9.0 (#4309): route through the AllowRuntimeCodeGeneration gate
            // so users can opt into AOT-friendly mode by setting the flag to
            // false (in which case missing pre-generated types throw rather
            // than silently triggering Roslyn).
            StaticOnlyCodeFileLoader.Initialize(
                _options.EventGraph, rules, _options.EventGraph, null,
                _options.AllowRuntimeCodeGeneration);

            _storage = _storage.AddOrUpdate(documentType, _options.EventGraph.Provider);

            return _options.EventGraph.Provider.As<DocumentProvider<T>>();
        }

        var mapping = _options.Storage.FindMapping(documentType);

        switch (mapping)
        {
            case DocumentMapping m:
            {
                // W3 (#4404): the closed-shape DocumentProvider is now the
                // only document-storage path — the Roslyn-emitted
                // DocumentProviderBuilder route has been removed.
                try
                {
                    var closedShape = ClosedShapeRegistration.BuildSupportedProvider<T>(m);
                    _storage = _storage.AddOrUpdate(documentType, closedShape);
                    return closedShape;
                }
                catch (Exception e)
                {
                    if (e is InvalidOperationException && !mapping.DocumentType.IsPublic)
                    {
                        throw new InvalidOperationException(
                            $"Requested document type '{mapping.DocumentType.FullNameInCode()}' must be scoped as 'public' in order to be used as a document type inside of Marten",
                            e);
                    }

                    throw;
                }
            }
            case SubClassMapping s:
            {
                var loader =
                    typeof(SubClassLoader<,,>).CloseAndBuildAs<ISubClassLoader<T>>(mapping.Root.DocumentType,
                        documentType,
                        mapping.IdType);

                var slot = loader.BuildPersistence(this, s);
                _storage = _storage.AddOrUpdate(documentType, slot);

                return slot;
            }
            case EventMapping em:
            {
                var storage = (IDocumentStorage<T>)em;
                var slot = new DocumentProvider<T>(null, storage, storage, storage, storage);
                _storage = _storage.AddOrUpdate(documentType, slot);

                return slot;
            }
            default:
                throw new NotSupportedException("Unable to build document persistence handlers for " +
                                                mapping.DocumentType.FullNameInCode());
        }
    }

    private interface ISubClassLoader<T> where T : notnull
    {
        DocumentProvider<T> BuildPersistence(IProviderGraph graph, SubClassMapping mapping);
    }

    private class SubClassLoader<TRoot, T, TId>: ISubClassLoader<T>
        where T : TRoot where TId : notnull where TRoot : notnull
    {
        public DocumentProvider<T> BuildPersistence(IProviderGraph graph, SubClassMapping mapping)
        {
            var inner = graph.StorageFor<TRoot>();

            var queryOnly =
                new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>)inner.QueryOnly, mapping);
            var lightweight =
                new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>)inner.Lightweight, mapping);
            var identityMap =
                new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>)inner.IdentityMap, mapping);
            var dirtyTracking =
                new SubClassDocumentStorage<T, TRoot, TId>((IDocumentStorage<TRoot, TId>)inner.DirtyTracking, mapping);

            var bulkLoader = new SubClassBulkLoader<T, TRoot>(inner.BulkLoader);
            return new DocumentProvider<T>(bulkLoader, queryOnly, lightweight, identityMap, dirtyTracking);
        }
    }
}
