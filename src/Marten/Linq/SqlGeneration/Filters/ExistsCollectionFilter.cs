#nullable enable
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

/// <summary>
///     Implemented by collection members that can supply a cheap per-document element
///     rowset for a correlated EXISTS filter, e.g.
///     "select elem.value as data from jsonb_array_elements(CAST(d.data ->> 'Lines' as jsonb)) as elem".
///     Null means the member cannot (yet) — the legacy explode/ctid strategy applies.
/// </summary>
internal interface IExistsElementSource
{
    string? ExplodedElementSource { get; }
}

/// <summary>
///     Renders a collection predicate that has no containment or jsonpath rendering
///     (member-to-member comparisons, DateTime values) as a correlated EXISTS over the
///     exploded elements instead of the old "explode everything into a CTE and
///     correlate on ctid" strategy — one scan, no materialization, composes at any
///     nesting depth, and NOT is just NOT EXISTS.
///     The derived table re-aliases itself as "d" with a "data" column on purpose:
///     inner fragments render member locators like "d.data ->> 'Number'" against the
///     element, while the correlated reference inside the derived table still sees the
///     outer document because a FROM item's alias is not visible inside itself.
/// </summary>
internal class ExistsCollectionFilter: ISqlFragment, IReversibleWhereFragment
{
    private readonly string _elementSource;

    public ExistsCollectionFilter(ICollectionMember member, ISqlFragment inner, string elementSource)
    {
        Member = member;
        Inner = inner;
        _elementSource = elementSource;
    }

    public ICollectionMember Member { get; }
    public ISqlFragment Inner { get; }

    public bool Not { get; set; }

    public void Apply(ICommandBuilder builder)
    {
        if (Not)
        {
            builder.Append("NOT(");
        }

        builder.Append("EXISTS (SELECT 1 FROM (");
        builder.Append(_elementSource);
        builder.Append(") AS d WHERE ");
        Inner.Apply(builder);
        builder.Append(")");

        if (Not)
        {
            builder.Append(")");
        }
    }

    public ISqlFragment Reverse()
    {
        Not = !Not;
        return this;
    }
}
