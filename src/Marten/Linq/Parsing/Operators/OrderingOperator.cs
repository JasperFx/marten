#nullable enable
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
        var memberArgument = expression.Arguments[1];

        // #5950: a key that reads nothing from the element, OrderBy(x => Settings.SortKey), is the same value for
        // every row. LINQ to Objects' sort is stable, so such a key leaves the order to the keys around it, and
        // a ThenBy() after it becomes the primary ordering. It is dropped from the ORDER BY rather than refused.
        if (isClosedKey(memberArgument))
        {
            return;
        }

        var ordering = new Ordering(memberArgument, Direction);
        if (expression.Arguments[1].Type == typeof(string))
        {
            var property = (string)expression.Arguments[1].ReduceToConstant().Value!;
            QueryableExtensions.GetSortProperty(ref property, out var sortOrder);
            ordering.Direction = sortOrder == "asc" ? OrderingDirection.Asc : OrderingDirection.Desc;
            ordering.MemberName = property;

        }


        if (expression.Arguments.Count == 3)
        {
            var comparer = expression.Arguments[2].Value() as StringComparer;

            if (comparer == StringComparer.OrdinalIgnoreCase || comparer == StringComparer.CurrentCultureIgnoreCase ||
                comparer == StringComparer.InvariantCultureIgnoreCase)
            {
                ordering.CasingRule = CasingRule.CaseInsensitive;
            }

            ordering.IsTransformed = true;
        }

        usage.OrderingExpressions.Insert(0, ordering);
    }

    private static bool isClosedKey(Expression argument)
    {
        while (argument is UnaryExpression { NodeType: ExpressionType.Quote } quote)
        {
            argument = quote.Operand;
        }

        // OrderBy("Name") names the member as a string; only a key selector can be a closed value
        return argument is LambdaExpression lambda && lambda.Body.IsClosedValue();
    }
}
