#nullable enable
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;

namespace Marten.Linq.Parsing.Operators;

public class SkipOperator: LinqOperator
{
    public SkipOperator(): base("Skip")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);
        usage.WriteOffset(expression.Arguments.Last().Value().As<int>());
    }
}
