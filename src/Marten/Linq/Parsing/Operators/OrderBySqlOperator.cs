#nullable enable
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

internal class OrderBySqlOperator : LinqOperator
{
    public OrderBySqlOperator() : base(nameof(QueryableExtensions.OrderBySql))
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var sql = expression.Arguments.Last().ReduceToConstant();
        var usage = query.CollectionUsageFor(expression);
        var ordering = new Ordering((string)sql.Value!);

        usage.OrderingExpressions.Insert(0, ordering);
    }
}

internal class ThenBySqlOperator : LinqOperator
{
    public ThenBySqlOperator() : base(nameof(QueryableExtensions.ThenBySql))
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var sql = expression.Arguments.Last().ReduceToConstant();
        var usage = query.CollectionUsageFor(expression);
        var ordering = new Ordering((string)sql.Value!);

        usage.OrderingExpressions.Insert(0, ordering);
    }
}
