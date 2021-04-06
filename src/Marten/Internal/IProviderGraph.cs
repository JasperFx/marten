using System;
using Baseline;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
#nullable enable
namespace Marten.Internal
{
    public interface IProviderGraph
    {
        DocumentProvider<T> StorageFor<T>();
    }

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

        private class StorageFinder<T>: IStorageFinder
        {
            public IDocumentStorage Find(IProviderGraph providers)
            {
                return providers.StorageFor<T>().Lightweight;
            }
        }

    }
}
