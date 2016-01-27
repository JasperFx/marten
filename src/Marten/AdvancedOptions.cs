using Marten.Schema;
using Marten.Services;

namespace Marten
{
    public class AdvancedOptions
    {
        public RequestCounterThreshold RequestThreshold { get; }

        public AdvancedOptions(IDocumentCleaner cleaner, RequestCounterThreshold requestThreshold)
        {
            RequestThreshold = requestThreshold;
            Clean = cleaner;
        }

        public IDocumentCleaner Clean { get; }
    }
}