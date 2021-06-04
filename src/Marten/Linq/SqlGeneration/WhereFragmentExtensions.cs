using System.Linq;
using Marten.Events.Archiving;
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

        public static bool SpecifiesEventArchivalStatus(this ISqlFragment query)
        {
            if (query.Flatten().OfType<IArchiveFilter>().Any()) return true;

            if (query.Contains(IsArchivedColumn.ColumnName)) return true;

            return false;
        }
    }
}
