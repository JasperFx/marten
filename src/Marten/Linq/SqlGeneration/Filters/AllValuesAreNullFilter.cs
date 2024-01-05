using Marten.Internal;
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;


internal class AllValuesAreNullFilter: ISubQueryFilter
{
    private static readonly ISqlFragment _filter = new WhereFragment("true = ALL (select unnest(data) is null)");

    private string _exportName;

    public AllValuesAreNullFilter(ICollectionMember member)
    {
        Member = member;
    }

    public ICollectionMember Member { get; }

    public void Apply(ICommandBuilder builder)
    {
        if (Not)
        {
            builder.Append("NOT(");
        }

        builder.Append("d.ctid in (select ctid from ");
        builder.Append(_exportName);
        builder.Append(")");

        if (Not)
        {
            builder.Append(")");
        }
    }

    /// <summary>
    ///     Psych! Should there be a NOT in front of the sub query
    /// </summary>
    public bool Not { get; set; }

    public ISqlFragment Reverse()
    {
        Not = !Not;
        return this;
    }

    public void PlaceUnnestAbove(IMartenSession session, SelectorStatement statement, ISqlFragment topLevelWhere = null)
    {
        // First need to unnest the collection into its own recordset
        var unnest = new ExplodeCollectionStatement(session, statement, Member.ArrayLocator) { Where = topLevelWhere };

        // Second, filter the collection
        var filter = new FilterStatement(session, unnest, _filter);

        _exportName = filter.ExportName;
    }
}


