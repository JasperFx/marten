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
        private readonly ITenant _tenant;

        public SpecificTenantFilter(ITenant tenant)
        {
            _tenant = tenant;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append($"d.{TenantIdColumn.Name} = ");
            builder.AppendParameter(_tenant.TenantId);
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}
