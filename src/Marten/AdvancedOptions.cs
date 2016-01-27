using Marten.Schema;
using Marten.Services;

namespace Marten
{
    public class AdvancedOptions
    {
        public RequestCounterThreshold RequestThreshold { get; }
        private readonly IDocumentCleaner _cleaner;

        public AdvancedOptions(IDocumentCleaner cleaner, RequestCounterThreshold requestThreshold)
        {
            RequestThreshold = requestThreshold;
            _cleaner = cleaner;
        }

        public IDocumentCleaner Clean
        {
            get { return _cleaner; }
        }
    }
}