#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

internal class OrderByNgramRankOperator : LinqOperator
{
    public OrderByNgramRankOperator() : base(nameof(QueryableExtensions.OrderByNgramRank))
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);

        // expression.Arguments[1] is the member lambda (x => x.SearchField)
        // expression.Arguments[2] is the search term constant
        var memberLambda = (LambdaExpression)expression.Arguments[1].UnBox();
        var searchTerm = (string)(expression.Arguments[2].ReduceToConstant().Value ?? "");

        var memberBody = memberLambda.Body;
        if (memberBody is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            memberBody = unary.Operand;
        }

        // Store the pieces for deferred resolution during BuildExpression
        var ordering = new Ordering(memberBody, OrderingDirection.Desc)
        {
            NgramRankSearchTerm = searchTerm,
            NgramRankMemberExpression = memberBody,
            NgramRankOptions = usage.Options,
            IsTransformed = true
        };

        usage.OrderingExpressions.Insert(0, ordering);
    }
}
