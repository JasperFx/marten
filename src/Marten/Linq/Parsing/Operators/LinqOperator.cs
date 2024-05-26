#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public abstract class LinqOperator
{
    public LinqOperator(string methodName)
    {
        MethodName = methodName;
    }

    public string MethodName { get; }

    public abstract void Apply(ILinqQuery query, MethodCallExpression expression);
}
