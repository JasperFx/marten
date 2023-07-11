using System;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public class OrderingOperator: LinqOperator
{
    public OrderingOperator(string methodName, OrderingDirection direction): base(methodName)
    {
        Direction = direction;
    }

    public OrderingDirection Direction { get; }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);
        var newOrdering = new Ordering(expression.Arguments[1], Direction);
        if (expression.Arguments.Count == 3)
        {
            var comparer = expression.Arguments[2].Value() as StringComparer;

            if (comparer == StringComparer.OrdinalIgnoreCase || comparer == StringComparer.CurrentCultureIgnoreCase ||
                comparer == StringComparer.InvariantCultureIgnoreCase)
            {
                newOrdering.CasingRule = CasingRule.CaseInsensitive;
            }
        }

        usage.OrderingExpressions.Insert(0, newOrdering);
    }
}
