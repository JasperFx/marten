#nullable enable
using System;
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public class GroupJoinOperator: LinqOperator
{
    public GroupJoinOperator(): base("GroupJoin")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        // GroupJoin signature: source.GroupJoin(inner, outerKeySelector, innerKeySelector, resultSelector)
        // expression.Arguments[0] = outer source
        // expression.Arguments[1] = inner source
        // expression.Arguments[2] = outer key selector
        // expression.Arguments[3] = inner key selector
        // expression.Arguments[4] = result selector

        // Save the current usage (created by SelectMany, if present) before creating the outer usage
        var selectManyUsage = query.CurrentUsage;

        var usage = query.CollectionUsageFor(expression);

        var innerSource = expression.Arguments[1];
        var outerKeyExpr = expression.Arguments[2].UnBox();
        var innerKeyExpr = expression.Arguments[3].UnBox();
        var resultSelectorExpr = expression.Arguments[4].UnBox();

        // Determine inner element type from the inner source's IQueryable<T> generic arg
        var innerElementType = innerSource.Type.GetGenericArguments()[0];

        var groupJoinData = new GroupJoinData
        {
            InnerSourceExpression = innerSource,
            OuterKeySelector = (LambdaExpression)outerKeyExpr,
            InnerKeySelector = (LambdaExpression)innerKeyExpr,
            ResultSelector = (LambdaExpression)resultSelectorExpr,
            InnerElementType = innerElementType
        };

        // If SelectMany was processed before us (it's the outermost operator),
        // extract the flattened result selector and detect DefaultIfEmpty
        if (selectManyUsage?.SelectManyCallExpression != null)
        {
            var selectManyExpr = selectManyUsage.SelectManyCallExpression;

            // 3-arg SelectMany: [source, collectionSelector, resultSelector]
            if (selectManyExpr.Arguments.Count >= 3)
            {
                var resultSelector = selectManyExpr.Arguments[2].UnBox();
                if (resultSelector is LambdaExpression resultLambda)
                {
                    groupJoinData.FlattenedResultSelector = resultLambda;
                }

                // Check collection selector for DefaultIfEmpty
                var collectionSelector = selectManyExpr.Arguments[1].UnBox();
                if (collectionSelector is LambdaExpression collLambda)
                {
                    groupJoinData.IsLeftJoin = ContainsDefaultIfEmpty(collLambda.Body);
                }
            }
            else if (selectManyExpr.Arguments.Count == 2)
            {
                // 2-arg SelectMany: [source, collectionSelector]
                // The collection selector is also the result - no separate result selector
                var collectionSelector = selectManyExpr.Arguments[1].UnBox();
                if (collectionSelector is LambdaExpression collLambda)
                {
                    groupJoinData.IsLeftJoin = ContainsDefaultIfEmpty(collLambda.Body);
                }
            }
        }

        usage.GroupJoinData = groupJoinData;
    }

    private static bool ContainsDefaultIfEmpty(Expression expression)
    {
        if (expression is MethodCallExpression methodCall)
        {
            if (methodCall.Method.Name == "DefaultIfEmpty")
            {
                return true;
            }

            // Check nested calls
            foreach (var arg in methodCall.Arguments)
            {
                if (ContainsDefaultIfEmpty(arg))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

internal static class ExpressionUnboxExtensions
{
    /// <summary>
    /// Unwraps Quote/UnaryExpression to get the underlying LambdaExpression
    /// </summary>
    public static Expression UnBox(this Expression expression)
    {
        while (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Quote)
        {
            expression = unary.Operand;
        }

        return expression;
    }
}
