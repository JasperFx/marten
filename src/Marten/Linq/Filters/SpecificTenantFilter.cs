using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Util;
using NpgsqlTypes;

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
            builder.Append($"d.{TenantIdColumn.Name} = :");
            var parameter = builder.AddParameter(_tenant.TenantId, NpgsqlDbType.Varchar);
            builder.Append(parameter.ParameterName);
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}
