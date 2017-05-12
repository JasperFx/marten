using Marten.Schema.Arguments;
using Marten.Storage;

namespace Marten.Linq
{
    public class TenantWhereFragment : WhereFragment
    {
        public TenantWhereFragment() : base($"d.{TenantIdColumn.Name} = :{TenantIdArgument.ArgName}")
        {
        }
    }
}