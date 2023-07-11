using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

/// <summary>
///     SQL WHERE fragment for a specific tenant
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
