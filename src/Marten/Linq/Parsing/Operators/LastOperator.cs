using System;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public class LastOperator: LinqOperator
{
    public LastOperator(): base("Last")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        throw new InvalidOperationException(
            "Marten does not support Last() or LastOrDefault() queries. Please reverse the ordering and use First()/FirstOrDefault() instead");
    }
}

public class LastOrDefaultOperator: LinqOperator
{
    public LastOrDefaultOperator(): base("LastOrDefault")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        throw new InvalidOperationException(
            "Marten does not support Last() or LastOrDefault() queries. Please reverse the ordering and use First()/FirstOrDefault() instead");
    }
}
