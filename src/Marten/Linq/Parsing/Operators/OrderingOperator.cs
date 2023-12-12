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

        var ordering = new Ordering(memberArgument, Direction);
        if (expression.Arguments[1].Type == typeof(string))
        {
            var property = (string)expression.Arguments[1].ReduceToConstant().Value;
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
}
