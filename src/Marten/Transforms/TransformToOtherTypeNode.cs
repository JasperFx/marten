using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Transforms
{
    public class TransformToOtherMatcher: IMethodCallMatcher
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

    public class TransformToOtherTypeNode: ResultOperatorExpressionNodeBase
    {
        public static MethodInfo[] SupportedMethods =
            typeof(TransformExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name == nameof(TransformExtensions.TransformTo)).ToArray();

        private readonly ResultOperatorBase _operator;

        public TransformToOtherTypeNode(MethodCallExpressionParseInfo parseInfo, Expression transform, Expression optionalSelector) : base(parseInfo, transform as LambdaExpression, optionalSelector as LambdaExpression)
        {
            var transformName = transform.As<ConstantExpression>().Value.As<string>();

            _operator = typeof(TransformToOtherTypeOperator<>).CloseAndBuildAs<ResultOperatorBase>(transformName,
                parseInfo.ParsedExpression.Type.GetGenericArguments()[0]);
        }

        protected override ResultOperatorBase CreateResultOperator(ClauseGenerationContext clauseGenerationContext)
        {
            return _operator;
        }

        public override Expression Resolve(ParameterExpression inputParameter, Expression expressionToBeResolved,
            ClauseGenerationContext clauseGenerationContext)
        {
            return Source.Resolve(
                inputParameter,
                expressionToBeResolved,
                clauseGenerationContext);
        }
    }
}
