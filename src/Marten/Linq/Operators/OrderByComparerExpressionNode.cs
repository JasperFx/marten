using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Linq.Operators;

internal class OrderByComparerExpressionNode: MethodCallExpressionNodeBase
{
    public static readonly MethodInfo[] SupportedMethods =
        typeof(Queryable).GetMethods()
            .Where(m => m.Name == nameof(Queryable.OrderBy) || m.Name == nameof(Queryable.OrderByDescending) ||
                        m.Name == nameof(Queryable.ThenBy) || m.Name == nameof(Queryable.ThenByDescending))
            .Where(x => x.GetParameters().Length == 3)
            .ToArray();

    private readonly ResolvedExpressionCache<Expression> _cachedSelector;
    private readonly LambdaExpression _keySelector;

    private readonly OrderingDirection _orderingDirection;

    public OrderByComparerExpressionNode(MethodCallExpressionParseInfo parseInfo, LambdaExpression keySelector,
        ConstantExpression constantExpression)
        : base(parseInfo)
    {
        _keySelector = keySelector;
        _cachedSelector = new ResolvedExpressionCache<Expression>(this);

        _orderingDirection = parseInfo.ParsedExpression.Method.Name == nameof(Queryable.OrderBy) ||
                             parseInfo.ParsedExpression.Method.Name == nameof(Queryable.ThenBy)
            ? OrderingDirection.Asc
            : OrderingDirection.Desc;

        ConstantExpression = constantExpression;
    }

    public ConstantExpression ConstantExpression { get; }

    public override Expression Resolve(ParameterExpression inputParameter, Expression expressionToBeResolved,
        ClauseGenerationContext clauseGenerationContext)
    {
        return Source.Resolve(inputParameter, expressionToBeResolved, clauseGenerationContext);
    }

    protected override void ApplyNodeSpecificSemantics(QueryModel queryModel,
        ClauseGenerationContext clauseGenerationContext)
    {
        if (_keySelector.ReturnType != typeof(string))
        {
            throw new ArgumentException("Only strings are supported when providing order comparer");
        }

        bool caseInsensitive;
        if (ReferenceEquals(ConstantExpression.Value, StringComparer.InvariantCultureIgnoreCase)
            || ReferenceEquals(ConstantExpression.Value, StringComparer.OrdinalIgnoreCase))
        {
            caseInsensitive = true;
        }
        else if (ReferenceEquals(ConstantExpression.Value, StringComparer.InvariantCulture)
                 || ReferenceEquals(ConstantExpression.Value, StringComparer.Ordinal))
        {
            caseInsensitive = false;
        }
        else
        {
            throw new ArgumentException(
                "Only ordinal and invariant StringComparer static comparer members are allowed as comparer");
        }

        var orderByClause = new OrderByComparerClause(caseInsensitive,
            new Ordering(GetResolvedKeySelector(clauseGenerationContext), _orderingDirection));
        queryModel.BodyClauses.Add(orderByClause);
    }

    private Expression GetResolvedKeySelector(ClauseGenerationContext clauseGenerationContext)
    {
        return _cachedSelector.GetOrCreate(r =>
            r.GetResolvedExpression(_keySelector.Body, _keySelector.Parameters[0], clauseGenerationContext));
    }
}
