using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Linq
{
    public class AsJsonMatcher: IMethodCallMatcher
    {
        public bool TryMatch(MethodCallExpression expression, ExpressionVisitor selectorVisitor,
            out ResultOperatorBase op)
        {
            if (expression.Method.Name == nameof(CompiledQueryExtensions.AsJson))
            {
                var argument = expression.Arguments.FirstOrDefault();

                if (!(argument is QuerySourceReferenceExpression)) selectorVisitor.Visit(argument);

                op = AsJsonResultOperator.Flyweight;
                return true;
            }

            op = null;
            return false;
        }
    }

    public class AsJsonExpressionNode: ResultOperatorExpressionNodeBase
    {
        public static MethodInfo[] SupportedMethods =
            typeof(CompiledQueryExtensions).GetMethods().Where(m => m.Name == nameof(CompiledQueryExtensions.AsJson)).ToArray();

        public AsJsonExpressionNode(
            MethodCallExpressionParseInfo parseInfo)
            : base(parseInfo, null, null) { }

        protected override ResultOperatorBase CreateResultOperator(
            ClauseGenerationContext clauseGenerationContext)
        {
            return AsJsonResultOperator.Flyweight;
        }

        public override Expression Resolve(
            ParameterExpression inputParameter,
            Expression expressionToBeResolved,
            ClauseGenerationContext clauseGenerationContext)
        {
            return Source.Resolve(
                inputParameter,
                expressionToBeResolved,
                clauseGenerationContext);
        }


    }
}
