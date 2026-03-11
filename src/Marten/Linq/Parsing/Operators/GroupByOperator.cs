#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public class GroupByOperator: LinqOperator
{
    public GroupByOperator(): base("GroupBy")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        // GroupBy signature: source.GroupBy(keySelector)
        // expression.Arguments[0] = source
        // expression.Arguments[1] = key selector

        var usage = query.CollectionUsageFor(expression);

        var keyExpr = expression.Arguments[1].UnBox();

        usage.GroupByData = new GroupByData
        {
            KeySelector = (LambdaExpression)keyExpr
        };
    }
}
