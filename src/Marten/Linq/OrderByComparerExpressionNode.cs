using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Linq
{
    internal class OrderByComparerExpressionNode: ResultOperatorExpressionNodeBase
    {
        private readonly ResolvedExpressionCache<Expression> cachedSelector;
        private readonly LambdaExpression keySelector;

        public static readonly MethodInfo[] SupportedMethods =
            typeof(Queryable).GetMethods()
                .Where(m => m.Name == nameof(Queryable.OrderBy) || m.Name == nameof(Queryable.OrderByDescending) || m.Name == nameof(Queryable.ThenBy) || m.Name == nameof(Queryable.ThenByDescending))
                .Where(x => x.GetParameters().Length == 3)
                .ToArray();

        private readonly OrderingDirection orderingDirection;

        public OrderByComparerExpressionNode(
            MethodCallExpressionParseInfo parseInfo,
            LambdaExpression keySelector,
            ConstantExpression constantExpression)
            : base(parseInfo, null, null)
        {
            ConstantExpression = constantExpression;
            this.keySelector = keySelector;
            cachedSelector = new ResolvedExpressionCache<Expression>(this);

            orderingDirection = parseInfo.ParsedExpression.Method.Name == nameof(Queryable.OrderBy) || parseInfo.ParsedExpression.Method.Name == nameof(Queryable.ThenBy)
                ? OrderingDirection.Asc
                : OrderingDirection.Desc;
        }

        public ConstantExpression ConstantExpression { get; }

        protected override ResultOperatorBase CreateResultOperator(
            ClauseGenerationContext clauseGenerationContext)
        {
            return new OrderByComparerOperator(null, ConstantExpression);
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

        protected override void ApplyNodeSpecificSemantics(
            QueryModel queryModel,
            ClauseGenerationContext clauseGenerationContext)
        {
            if (keySelector.ReturnType != typeof(string))
            {
                throw new ArgumentException("Only strings are supported when providing order comparer");
            }

            bool caseSensitive;
            if (ReferenceEquals(ConstantExpression.Value, StringComparer.OrdinalIgnoreCase)
                || ReferenceEquals(ConstantExpression.Value, StringComparer.InvariantCultureIgnoreCase)
                || ReferenceEquals(ConstantExpression.Value, StringComparer.OrdinalIgnoreCase))
            {
                caseSensitive = false;
            }
            else if (ReferenceEquals(ConstantExpression.Value, StringComparer.Ordinal)
                     || ReferenceEquals(ConstantExpression.Value, StringComparer.InvariantCulture)
                     || ReferenceEquals(ConstantExpression.Value, StringComparer.Ordinal))
            {
                caseSensitive = false;
            }
            else
            {
                throw new ArgumentException("Only standard StringComparer static comparer members are allowed as comparer");
            }

            var orderByClause = new OrderByComparerClause(caseSensitive, new Ordering(GetResolvedKeySelector(clauseGenerationContext), orderingDirection));
            queryModel.BodyClauses.Add(orderByClause);
        }

        private Expression GetResolvedKeySelector(
            ClauseGenerationContext clauseGenerationContext) => cachedSelector.GetOrCreate(r => r.GetResolvedExpression(keySelector.Body, keySelector.Parameters[0], clauseGenerationContext));

    }
}
