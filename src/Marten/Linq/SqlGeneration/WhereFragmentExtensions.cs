using System.Linq;
using Marten.Linq.Filters;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    internal static class WhereFragmentExtensions
    {
        public static bool SpecifiesTenant(this ISqlFragment fragment)
        {
            return fragment.Flatten().OfType<ITenantWhereFragment>().Any();
        }
    }
}
