using System.Linq;
using Marten.Events.Archiving;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal static class WhereFragmentExtensions
{
    public static bool SpecifiesTenant(this ISqlFragment fragment)
    {
        if (fragment is ITenantWhereFragment)
        {
            return true;
        }

        if (fragment is CompoundWhereFragment cwf)
        {
            foreach (var child in cwf.Children)
            {
                if (SpecifiesTenant(child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool SpecifiesEventArchivalStatus(this ISqlFragment query)
    {
        if (query.Flatten().OfType<IArchiveFilter>().Any())
        {
            return true;
        }

        if (query.Contains(IsArchivedColumn.ColumnName))
        {
            return true;
        }

        return false;
    }
}
