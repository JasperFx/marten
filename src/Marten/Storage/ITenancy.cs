using Marten.Schema;

namespace Marten.Storage
{
    public enum TenancyStyle
    {
        /// <summary>
        /// No multi-tenancy, the default mode
        /// </summary>
        Single,

        /// <summary>
        /// Multi-tenanted within the same database/schema through a tenant id
        /// </summary>
        Conjoined,

        /// <summary>
        /// Multi-tenanted through separate databases or schemas
        /// </summary>
        Separate
    }

    public interface ITenancy
    {
        ITenant this[string tenantId] { get; }
        ITenant Default { get; }

        void Initialize();

        IDocumentCleaner Cleaner { get; }

        IDocumentSchema Schema { get; }

        TenancyStyle Style { get; }
    }
}