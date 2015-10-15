using Marten.Schema;

namespace Marten
{
    public interface IDocumentStore
    {
        IDocumentSchema Schema { get; }
        AdvancedOptions Advanced { get; }
    }

    public class AdvancedOptions
    {
        private readonly IDocumentCleaner _cleaner;

        public AdvancedOptions(IDocumentCleaner cleaner)
        {
            _cleaner = cleaner;
        }

        public IDocumentCleaner Clean
        {
            get { return _cleaner; }
        }
    }
}