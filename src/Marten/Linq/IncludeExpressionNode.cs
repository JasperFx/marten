using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Linq
{
    public class IncludeExpressionNode: ResultOperatorExpressionNodeBase
    {
        public LambdaExpression IdSource { get; set; }
        public Expression Callback { get; set; }

        public static MethodInfo[] SupportedMethods =
            typeof(IMartenQueryable<>).GetMethods().Where(m => m.Name == "Include").ToArray();

        public IncludeExpressionNode(
            MethodCallExpressionParseInfo parseInfo, LambdaExpression idSource, Expression callback)
            : base(parseInfo, null, null)
        {
            IdSource = idSource;
            Callback = callback;
        }

        protected override ResultOperatorBase CreateResultOperator(
            ClauseGenerationContext clauseGenerationContext)
        {
            return new IncludeResultOperator(IdSource, Callback);
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
