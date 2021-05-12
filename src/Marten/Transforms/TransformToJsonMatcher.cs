using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Transforms
{
    internal class TransformToJsonMatcher: IMethodCallMatcher
    {
        public bool TryMatch(MethodCallExpression expression, ExpressionVisitor selectorVisitor,
            out ResultOperatorBase op)
        {
            if (expression.Method.Name == nameof(TransformExtensions.TransformToJson))
            {
                var transformName = (string)expression.Arguments.Last().As<ConstantExpression>().Value;
                op = new TransformToJsonResultOperator(transformName);
                return true;
            }

            op = null;
            return false;
        }
    }
}