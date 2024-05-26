#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

internal class AnyOperator: LinqOperator
{
    public AnyOperator(): base("Any")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);
        usage.AddWhereClause(expression);
        usage.IsAny = true;
    }
}
