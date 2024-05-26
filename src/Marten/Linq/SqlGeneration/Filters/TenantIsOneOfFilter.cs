#nullable enable
using Marten.Storage.Metadata;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal class TenantIsOneOfFilter: ISqlFragment, ITenantFilter
{
    private static readonly string _filter = $"{TenantIdColumn.Name} = ANY(:?)";

    private readonly string[] _values;

    public TenantIsOneOfFilter(string[] values)
    {
        _values = values;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(TenantIdColumn.Name);
        builder.Append(" = ANY(");
        builder.AppendParameter(_values, NpgsqlDbType.Array | NpgsqlDbType.Varchar);
        builder.Append(')');
    }

}
