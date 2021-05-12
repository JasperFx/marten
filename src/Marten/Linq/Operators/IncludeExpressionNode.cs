using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Linq.Operators
{
    internal class IncludeExpressionNode: ResultOperatorExpressionNodeBase
    {
        public static MethodInfo[] SupportedMethods =
            typeof(QueryableExtensions).GetMethods().Where(m => m.Name == "Include")
                .Concat(typeof(IMartenQueryable<>).GetMethods().Where(m => m.Name == "Include")).ToArray();


        public IncludeExpressionNode(
            MethodCallExpressionParseInfo parseInfo, Expression connectingField, ConstantExpression include)
            : base(parseInfo, null, null)
        {
            ConnectingField = connectingField;
            IncludeExpression = include;
        }

        public ConstantExpression IncludeExpression { get; set; }

        public Expression ConnectingField { get; set; }

        protected override ResultOperatorBase CreateResultOperator(
            ClauseGenerationContext clauseGenerationContext)
        {
            return new IncludeResultOperator(ConnectingField, IncludeExpression);
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
