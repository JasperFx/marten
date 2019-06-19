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
    }
}
