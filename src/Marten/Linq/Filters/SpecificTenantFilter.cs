using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Filters
{
    /// <summary>
    /// SQL WHERE fragment for a specific tenant
    /// </summary>
    internal class SpecificTenantFilter: ISqlFragment
    {
        private readonly string _tenantId;

        public SpecificTenantFilter(string tenantId)
        {
            _tenantId = tenantId;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append($"d.{TenantIdColumn.Name} = ");
            builder.AppendParameter(_tenantId);
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}
