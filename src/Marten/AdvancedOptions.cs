using System;
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
        ///     The original StoreOptions used to configure the current DocumentStore
        /// </summary>
        [Obsolete("This needs to be some new kind of readonly view")]
        public StoreOptions Options => _store.Options;

        /// <summary>
        ///     Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean => _store.Tenants.Cleaner;


        public ISerializer Serializer => _store.Serializer;
    }
}