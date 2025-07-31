#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

// This exists strictly for compiled queries. This is not shocking!!!
internal class IncludeOperator: LinqOperator
{
    public IncludeOperator(): base("Include")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        // Should be IMartenQueryable<T>
        var elementType = (expression.Object ?? expression.Arguments[0]).Type.GetGenericArguments()[0];

        var usage = query.CollectionUsageFor(elementType);
        usage.IncludeExpressions.Add(expression);
    }
}
