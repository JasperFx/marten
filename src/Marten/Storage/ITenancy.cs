using Marten.Schema;

namespace Marten.Storage
{
    public interface ITenancy
    {
        ITenant this[string tenantId] { get; }
        ITenant Default { get; }

        void Initialize();

        IDocumentCleaner Cleaner { get; }

        IDocumentSchema Schema { get; }
    }
}