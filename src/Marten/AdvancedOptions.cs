using Marten.Schema;

namespace Marten
{
    public class AdvancedOptions
    {
        /// <summary>
        /// The original StoreOptions used to configure the current DocumentStore
        /// </summary>
        public StoreOptions Options { get; }

        public AdvancedOptions(IDocumentCleaner cleaner, StoreOptions options)
        {
            Options = options;
            Clean = cleaner;
        }

        /// <summary>
        /// Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean { get; }
    }
}