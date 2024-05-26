#nullable enable
using Marten.Internal;
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal interface ISubQueryFilter : IReversibleWhereFragment
{
    void PlaceUnnestAbove(IMartenSession session, SelectorStatement statement,
        ISqlFragment? topLevelWhere = null);
}

internal class SubQueryFilter: ISubQueryFilter
{
    private string? _exportName;

    public SubQueryFilter(ICollectionMember member, ISqlFragment inner)
    {
        Member = member;
        Inner = inner;
    }

    public ICollectionMember Member { get; }
    public ISqlFragment Inner { get; }

    /// <summary>
    ///     Psych! Should there be a NOT in front of the sub query
    /// </summary>
    public bool Not { get; set; }

    public ISqlFragment Reverse()
    {
        Not = !Not;
        return this;
    }

    void ISqlFragment.Apply(ICommandBuilder builder)
    {
        if (Not)
        {
            builder.Append("NOT(");
        }

        builder.Append("d.ctid in (select ctid from ");
        builder.Append(_exportName!);
        builder.Append(")");

        if (Not)
        {
            builder.Append(")");
        }
    }

    public void PlaceUnnestAbove(IMartenSession session, SelectorStatement statement,
        ISqlFragment? topLevelWhere = null)
    {
        // First need to unnest the collection into its own recordset
        var unnest = new ExplodeCollectionStatement(session, statement, Member.ExplodeLocator) { Where = topLevelWhere };

        // Second, filter the collection
        var filter = new FilterStatement(session, unnest, Inner);

        _exportName = filter.ExportName;
    }
}
