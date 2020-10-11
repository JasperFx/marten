using Marten.Linq.SqlGeneration;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;

namespace Marten.Linq.Filters
{
    public class TenantWhereFragment: ISqlFragment
    {
        public static readonly string Filter = $"d.{TenantIdColumn.Name} = :{TenantIdArgument.ArgName}";

        public static readonly TenantWhereFragment Instance = new TenantWhereFragment();

        public void Apply(CommandBuilder builder)
        {
            builder.Append(Filter);
            builder.AddNamedParameter(TenantIdArgument.ArgName, "");
        }

        public bool Contains(string sqlText)
        {
            return Filter.Contains(sqlText);
        }
    }
}
