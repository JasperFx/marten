using Marten.Schema.Arguments;
using Marten.Storage;

namespace Marten.Linq
{
    public class TenantWhereFragment : WhereFragment
    {
        public static readonly string Filter = $"d.{TenantIdColumn.Name} = :{TenantIdArgument.ArgName}";

        public TenantWhereFragment() : base(Filter)
        {
        }
    }
}