#nullable enable
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;

namespace Marten.Linq.Parsing.Operators;

public class TakeOperator: LinqOperator
{
    public TakeOperator(): base("Take")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);
        usage.WriteLimit(expression.Arguments.Last().Value().As<int>());
    }
}
