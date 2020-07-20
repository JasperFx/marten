using System;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Remotion.Linq.Clauses.Expressions;

namespace Marten.Linq
{
    public static class ExpressionExtensions
    {
        public static object Value(this Expression expression)
        {
            if (expression is PartialEvaluationExceptionExpression)
            {
                var partialEvaluationExceptionExpression = expression.As<PartialEvaluationExceptionExpression>();
                var inner = partialEvaluationExceptionExpression.Exception;

                throw new BadLinqExpressionException($"Error in value expression inside of the query for '{partialEvaluationExceptionExpression.EvaluatedExpression}'. See the inner exception:", inner);
            }

            if (expression is ConstantExpression c)
            {
                return c.Value;
            }

            throw new NotSupportedException($"The Expression is {expression} of type {expression.GetType().Name}");
        }

        public static bool IsValueExpression(this Expression expression)
        {
            Type[] valueExpressionTypes = {
                typeof (ConstantExpression), typeof (PartialEvaluationExceptionExpression)
            };
            return valueExpressionTypes.Any(t => t.IsInstanceOfType(expression));
        }
    }
}
