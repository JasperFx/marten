#nullable enable
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

/// <summary>
///     Implemented by collection members that can translate aggregate functions
///     (Sum/Min/Max/Average) over their elements inside a Where() clause
/// </summary>
internal interface IAggregateableCollection
{
    IComparableMember ParseComparableForAggregate(string function, Expression? selector);
}

/// <summary>
///     Translates Where(x => x.Items.Sum(i => i.Qty) &gt; n) and friends into a
///     correlated scalar subquery over the exploded collection elements:
///     (SELECT SUM(CAST(d.data ->> 'Qty' as integer)) FROM (&lt;elements&gt;) as d) &gt; :n
/// </summary>
internal class CollectionAggregateComparable: IComparableMember
{
    private readonly string _function;
    private readonly string _valueLocator;
    private readonly string _elementSource;

    public CollectionAggregateComparable(string function, string valueLocator, string elementSource)
    {
        _function = function.ToUpperInvariant() == "AVERAGE" ? "AVG" : function.ToUpperInvariant();
        _valueLocator = valueLocator;
        _elementSource = elementSource;
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        return new CollectionAggregateFilter(_function, _valueLocator, _elementSource, op, constant);
    }
}

internal class CollectionAggregateFilter: ISqlFragment
{
    private readonly ConstantExpression _constant;
    private readonly string _elementSource;
    private readonly string _function;
    private readonly string _op;
    private readonly string _valueLocator;

    public CollectionAggregateFilter(string function, string valueLocator, string elementSource, string op,
        ConstantExpression constant)
    {
        _function = function;
        _valueLocator = valueLocator;
        _elementSource = elementSource;
        _op = op;
        _constant = constant;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("(SELECT ");

        // LINQ-to-objects Sum() over an empty collection is 0, not null. Min/Max/
        // Average over an empty collection throw in C#; SQL null makes the
        // comparison false, which is the closest translation a filter can offer
        if (_function == "SUM")
        {
            builder.Append("COALESCE(SUM(");
            builder.Append(_valueLocator);
            builder.Append("), 0)");
        }
        else
        {
            builder.Append(_function);
            builder.Append("(");
            builder.Append(_valueLocator);
            builder.Append(")");
        }

        builder.Append(" FROM (");
        builder.Append(_elementSource);
        builder.Append(") as d) ");
        builder.Append(_op);
        builder.Append(" ");
        builder.AppendParameter(_constant.Value());
    }
}
