using Marten.Schema;
using Marten.Services;

namespace Marten
{
    public class AdvancedOptions
    {
        public RequestCounterThreshold RequstThreshold { get; }
        private readonly IDocumentCleaner _cleaner;

        public AdvancedOptions(IDocumentCleaner cleaner, RequestCounterThreshold requstThreshold)
        {
            RequstThreshold = requstThreshold;
            _cleaner = cleaner;
        }

        public IDocumentCleaner Clean
        {
            get { return _cleaner; }
        }
    }
}