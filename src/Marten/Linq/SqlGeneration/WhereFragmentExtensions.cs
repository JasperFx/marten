using System.Linq;
using Marten.Events.Archiving;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal static class WhereFragmentExtensions
{
    public static bool SpecifiesTenant(this ISqlFragment fragment)
    {
        if (fragment is ITenantFilter)
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

    public static bool TryFindTenantAwareFilter(this ISqlFragment fragment, out ITenantFilter tenantFilter)
    {
        if (fragment is SelectorStatement statement)
        {
            foreach (var where in statement.Wheres)
            {
                if (where.TryFindTenantAwareFilter(out tenantFilter))
                {
                    return true;
                }
            }
        }

        if (fragment is ITenantFilter f)
        {
            tenantFilter = f;
            return true;
        }

        if (fragment is CompoundWhereFragment cwf)
        {
            foreach (var child in cwf.Children)
            {
                if (child.TryFindTenantAwareFilter(out tenantFilter))
                {
                    return true;
                }
            }
        }

        tenantFilter = default;
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
