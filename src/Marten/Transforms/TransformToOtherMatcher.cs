using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Transforms
{
    internal class TransformToOtherMatcher: IMethodCallMatcher
    {
        public bool TryMatch(MethodCallExpression expression, ExpressionVisitor selectorVisitor,
            out ResultOperatorBase op)
        {
            if (expression.Method.Name == nameof(TransformExtensions.TransformTo))
            {
                var transformName = (string)expression.Arguments.Last().As<ConstantExpression>().Value;

                var selectedType = expression.Type;
                if (selectedType.Closes(typeof(IQueryable<>)))
                {
                    selectedType = selectedType.GetGenericArguments()[0];
                }

                op = typeof(TransformToOtherTypeOperator<>).CloseAndBuildAs<ResultOperatorBase>(transformName,
                    selectedType);

                return true;
            }

            op = null;
            return false;
        }
    }
}
