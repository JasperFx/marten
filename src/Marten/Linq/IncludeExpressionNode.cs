using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Internal.Linq.Includes;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Linq
{
    public class IncludeExpressionNode: ResultOperatorExpressionNodeBase
    {
        public IIncludePlan Include { get; }

        public static MethodInfo[] SupportedMethods =
            typeof(QueryableExtensions).GetMethods().Where(m => m.Name == "Include").ToArray();

        public IncludeExpressionNode(
            MethodCallExpressionParseInfo parseInfo, ConstantExpression include)
            : base(parseInfo, null, null)
        {
            Include = (IIncludePlan) include.Value;
        }

        protected override ResultOperatorBase CreateResultOperator(
            ClauseGenerationContext clauseGenerationContext)
        {
            return new IncludeResultOperator(Include);
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
