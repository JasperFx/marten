using System;
using Baseline;
using Marten.Internal;
using Marten.Schema;

namespace Marten
{
    public class AdvancedOptions
    {
        private readonly DocumentStore _store;

        public AdvancedOptions(DocumentStore store)
        {
            _store = store;
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
