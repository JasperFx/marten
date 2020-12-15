using System;
using Baseline;
using Marten.Internal;
using Marten.Schema;

namespace Marten
{
    /// <summary>
    /// Access to advanced, rarely used features of IDocumentStore
    /// </summary>
    public class AdvancedOptions
    {
        private readonly DocumentStore _store;

        internal AdvancedOptions(DocumentStore store)
        {
            _store = store;
        }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public void ResetHiloSequenceFloor<T>(long floor)
        {
            _store.Tenancy.Default.ResetHiloSequenceFloor<T>(floor);
        }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public void ResetHiloSequenceFloor<T>(string tenantId, long floor)
        {
            _store.Tenancy[tenantId].ResetHiloSequenceFloor<T>(floor);
        }

        /// <summary>
        ///     Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean => _store.Tenancy.Cleaner;

        public ISerializer Serializer => _store.Serializer;


        /// <summary>
        /// Access the generated source code Marten is using for a given
        /// document type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public IDocumentSourceCode SourceCodeForDocumentType(Type type)
        {
            var loader = typeof(DocumentSourceCodeLoader<>)
                .CloseAndBuildAs<IDocumentSourceCodeLoader>(type);

            return loader.Load(_store.Options.Providers);
        }

        internal interface IDocumentSourceCodeLoader
        {
            IDocumentSourceCode Load(IProviderGraph providers);
        }

        internal class DocumentSourceCodeLoader<T>: IDocumentSourceCodeLoader
        {
            public IDocumentSourceCode Load(IProviderGraph providers)
            {
                return providers.StorageFor<T>();
            }
        }
    }
}
