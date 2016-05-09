using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Linq
{
    public class IncludeExpressionNode : ResultOperatorExpressionNodeBase
    {
        public LambdaExpression IdSource { get; set; }
        public LambdaExpression Callback { get; set; }
        public ConstantExpression JoinType { get; set; }

        public static MethodInfo[] SupportedMethods =
            typeof (CompiledQueryExtensions).GetMethods().Where(m => m.Name == nameof(CompiledQueryExtensions.Include)).ToArray();

        public IncludeExpressionNode(
            MethodCallExpressionParseInfo parseInfo, LambdaExpression idSource, LambdaExpression callback,
            ConstantExpression joinType)
            : base(parseInfo, null, null)
        {
            IdSource = idSource;
            Callback = callback;
            JoinType = joinType;
        }

        protected override ResultOperatorBase CreateResultOperator(
            ClauseGenerationContext clauseGenerationContext)
        {
            return new IncludeResultOperator(IdSource,Callback,JoinType);
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