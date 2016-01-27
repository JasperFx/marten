using Marten.Schema;

namespace Marten
{
    public class AdvancedOptions
    {
        public StoreOptions Options { get; }

        public AdvancedOptions(IDocumentCleaner cleaner, StoreOptions options)
        {
            Options = options;
            Clean = cleaner;
        }

        public IDocumentCleaner Clean { get; }
    }
}