using System;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;
public class AllComparisonFilter: IReversibleWhereFragment
{
    private readonly ComparisonFilter _nestedFilter;

    public AllComparisonFilter(ComparisonFilter nestedFilter)
    {
        _nestedFilter = nestedFilter ?? throw new ArgumentNullException(nameof(nestedFilter));
    }

    public void Apply(CommandBuilder builder)
    {
        if (_nestedFilter.Right is CommandParameter { Value: null })
        {
            builder.Append(" true = ALL (SELECT unnest(data) is null)");
            return;
        }

        _nestedFilter.Right.Apply(builder);
        builder.Append(" ");
        builder.Append(_nestedFilter.Op);
        builder.Append($" ALL (data)");
    }

    public bool Contains(string sqlText) => _nestedFilter.Contains(sqlText);

    public ISqlFragment Reverse()
    {
        _nestedFilter.Reverse();
        return this;
    }
}
