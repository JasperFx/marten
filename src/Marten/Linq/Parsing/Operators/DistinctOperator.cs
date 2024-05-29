#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

internal class DistinctOperator: LinqOperator
{
    public DistinctOperator(): base("Distinct")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);
        usage.IsDistinct = true;
    }
}
