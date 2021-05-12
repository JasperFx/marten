using System.Linq.Expressions;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    /// <summary>
    /// Extension point to Marten's Linq support to add custom MethodInfo handling
    /// in the query creation
    /// </summary>
    internal interface IMethodCallMatcher
    {
        bool TryMatch(MethodCallExpression expression, ExpressionVisitor selectorVisitor,
            out ResultOperatorBase op);
    }
}
