#nullable enable
namespace Marten.Schema.Indexing.Unique
{
    public enum TenancyScope
    {
        /// <summary>
        /// The uniqueness of this index should be global for all tenants
        /// </summary>
        Global,

        /// <summary>
        /// The uniqueness of this index should be within one tenant
        /// </summary>
        PerTenant
    }
}
