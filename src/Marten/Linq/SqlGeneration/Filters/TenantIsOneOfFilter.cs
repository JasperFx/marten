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

    public void Apply(CommandBuilder builder)
    {
        var param = builder.AddParameter(_values);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        builder.Append(_filter.Replace("?", param.ParameterName));
    }

    public bool Contains(string sqlText)
    {
        return _filter.Contains(sqlText);
    }
}
