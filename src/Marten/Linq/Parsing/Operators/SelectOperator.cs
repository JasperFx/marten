#nullable enable
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public class SelectOperator: LinqOperator
{
    public SelectOperator(): base("Select")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);
        var select = expression.Arguments.Last();
        if (select is UnaryExpression e)
        {
            select = e.Operand;
        }

        if (select is LambdaExpression l)
        {
            select = l.Body;
        }

        usage.SelectExpression = select;
    }
}
