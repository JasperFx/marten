#nullable enable
using System;
using JasperFx.Core.Reflection;
using Marten.Internal.Storage;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public interface IProviderGraph
{
    DocumentProvider<T> StorageFor<T>() where T : notnull;

    void Append<T>(DocumentProvider<T> provider) where T : notnull;
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal static class ProviderGraphExtensions
{
    internal static IDocumentStorage StorageFor(this IProviderGraph providers, Type documentType)
    {
        return typeof(StorageFinder<>).CloseAndBuildAs<IStorageFinder>(documentType).Find(providers);
    }

    private interface IStorageFinder
    {
        IDocumentStorage Find(IProviderGraph providers);
    }

    private class StorageFinder<T>: IStorageFinder where T : notnull
    {
        public IDocumentStorage Find(IProviderGraph providers)
        {
            return providers.StorageFor<T>().Lightweight;
        }
    }
}
