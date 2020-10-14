using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Linq.Filters
{
    internal class TenantIsOneOfWhereFragment: ISqlFragment, ITenantWhereFragment
    {
        private static readonly string _filter = $"{TenantIdColumn.Name} = ANY(?)";

        private readonly string[] _values;

        public TenantIsOneOfWhereFragment(string[] values)
        {
            _values = values;
        }

        public void Apply(CommandBuilder builder)
        {
            var param = builder.AddParameter(_values);
            param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
            builder.Append(_filter.Replace("?", ":" + param.ParameterName));
        }

        public bool Contains(string sqlText)
        {
            return _filter.Contains(sqlText);
        }
    }
}
