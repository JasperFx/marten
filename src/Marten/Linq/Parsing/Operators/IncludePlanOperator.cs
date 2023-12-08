using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Includes;

namespace Marten.Linq.Parsing.Operators;

internal class IncludePlanOperator: LinqOperator
{
    public IncludePlanOperator(): base("IncludePlan")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        // Should be IMartenQueryable<T>
        var elementType = (expression.Object ?? expression.Arguments[0]).Type.GetGenericArguments()[0];

        var usage = query.CollectionUsageFor(elementType);

        usage.Includes.Add((IIncludePlan)expression.Arguments.Last().ReduceToConstant().Value);
    }
}
