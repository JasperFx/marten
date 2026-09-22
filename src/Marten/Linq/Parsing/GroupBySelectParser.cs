#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

/// <summary>
/// Parses the Select() projection on an IGrouping to build the SQL SELECT columns,
/// GROUP BY keys, and aggregate expressions (COUNT, SUM, MIN, MAX, AVG).
/// </summary>
internal class GroupBySelectParser: ExpressionVisitor
{
    private readonly ISerializer _serializer;
    private readonly Func<Expression, IQueryableMember> _memberFor;
    private readonly LambdaExpression _keySelector;
    private readonly ParameterExpression _groupingParameter;

    // For composite keys: maps anonymous type member name to IQueryableMember
    private readonly Dictionary<string, IQueryableMember> _keyMembers = new();
    // For simple keys: the single key member
    private IQueryableMember _simpleKeyMember;
    private bool _isCompositeKey;

    private string _currentField;
    private bool _hasStarted;

    public NewObject NewObject { get; private set; }
    public List<string> GroupByColumns { get; } = new();

    // For scalar select (e.g., .Select(g => g.Key) or .Select(g => g.Count()))
    public ISqlFragment ScalarFragment { get; private set; }
    public bool IsScalar { get; private set; }

    /// <param name="memberFor">
    /// How a member expression becomes a locator. Defaults to the document's own member collection; a
    /// GroupJoin hands in a resolver that addresses each side through its CTE alias, because "d." means
    /// nothing once the FROM is a join of two CTEs.
    /// </param>
    public GroupBySelectParser(
        ISerializer serializer,
        IQueryableMemberCollection collection,
        LambdaExpression keySelector,
        Expression selectBody,
        ParameterExpression groupingParameter,
        Func<Expression, IQueryableMember> memberFor = null)
    {
        _serializer = serializer;
        _memberFor = memberFor ?? (expression => collection.MemberFor(expression));
        _keySelector = keySelector;
        _groupingParameter = groupingParameter;

        NewObject = new NewObject(serializer);
        ParseKeySelector();
        Visit(selectBody);
    }

    private void ParseKeySelector()
    {
        var body = _keySelector.Body;

        if (body is NewExpression newExpr)
        {
            // Composite key: x => new { x.Color, x.Number }
            _isCompositeKey = true;
            var parameters = newExpr.Constructor!.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                var member = _memberFor(newExpr.Arguments[i]);
                _keyMembers[parameters[i].Name!] = member;
                GroupByColumns.Add(member.TypedLocator);
            }
        }
        else if (body is MemberInitExpression memberInit)
        {
            // Composite key with member init: x => new KeyClass { Color = x.Color }
            _isCompositeKey = true;
            foreach (var binding in memberInit.Bindings.OfType<MemberAssignment>())
            {
                var member = _memberFor(binding.Expression);
                _keyMembers[binding.Member.Name] = member;
                GroupByColumns.Add(member.TypedLocator);
            }
        }
        else
        {
            // Simple key: x => x.Color
            _isCompositeKey = false;
            _simpleKeyMember = _memberFor(body);
            GroupByColumns.Add(_simpleKeyMember.TypedLocator);
        }
    }

    protected override Expression VisitNew(NewExpression node)
    {
        if (_hasStarted)
        {
            // Nested new expression - not supported for now
            throw new BadLinqExpressionException(
                "Marten does not support nested constructors in GroupBy projections");
        }

        _hasStarted = true;

        var parameters = node.Constructor!.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            _currentField = parameters[i].Name;
            Visit(node.Arguments[i]);
        }

        return node;
    }

    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
        _hasStarted = true;

        // Visit constructor args first
        var parameters = node.NewExpression.Constructor!.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            _currentField = parameters[i].Name;
            Visit(node.NewExpression.Arguments[i]);
        }

        // Then visit member bindings
        foreach (var binding in node.Bindings.OfType<MemberAssignment>())
        {
            _currentField = binding.Member.Name;
            Visit(binding.Expression);
        }

        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Check if this is g.Key
        if (IsGroupingKeyAccess(node))
        {
            if (_isCompositeKey)
            {
                // g.Key for composite key - this shouldn't happen directly in a well-formed projection
                // But if it does, we can't represent the whole anonymous key as a single SQL expression
                throw new BadLinqExpressionException(
                    "Cannot select the entire composite GroupBy key directly. Access individual key members like g.Key.Color instead.");
            }

            if (_currentField != null)
            {
                NewObject.Members[_currentField] = _simpleKeyMember;
                _currentField = null;
            }
            else
            {
                // Scalar select: .Select(g => g.Key)
                IsScalar = true;
                ScalarFragment = _simpleKeyMember;
            }

            return node;
        }

        // Check if this is g.Key.PropertyName (composite key member access)
        if (node.Expression is MemberExpression innerMember && IsGroupingKeyAccess(innerMember))
        {
            var memberName = node.Member.Name;
            if (_keyMembers.TryGetValue(memberName, out var keyMember))
            {
                if (_currentField != null)
                {
                    NewObject.Members[_currentField] = keyMember;
                    _currentField = null;
                }
                else
                {
                    IsScalar = true;
                    ScalarFragment = keyMember;
                }

                return node;
            }

            throw new BadLinqExpressionException(
                $"Unknown composite key member '{memberName}' in GroupBy projection");
        }

        return base.VisitMember(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var aggregateSql = TryResolveAggregate(node);
        if (aggregateSql != null)
        {
            if (_currentField != null)
            {
                NewObject.Members[_currentField] = new LiteralSql(aggregateSql);
                _currentField = null;
            }
            else
            {
                IsScalar = true;
                ScalarFragment = new LiteralSql(aggregateSql);
            }

            return node;
        }

        return base.VisitMethodCall(node);
    }

    private string TryResolveAggregate(MethodCallExpression node)
    {
        var methodName = node.Method.Name;

        // Parameterless: g.Count(), g.LongCount()
        if (methodName is "Count" or "LongCount")
        {
            if (node.Arguments.Count == 1 && IsGroupingParameter(node.Arguments[0]))
            {
                return "count(*)";
            }

            // With predicate: g.Count(x => x.Flag)
            if (node.Arguments.Count == 2 && IsGroupingParameter(node.Arguments[0]))
            {
                var predicateSql = ResolvePredicate(node.Arguments[1]);
                return $"count(*) filter (where {predicateSql})";
            }
        }

        // Aggregate with selector: g.Sum(x => x.Number), g.Min(...), g.Max(...), g.Average(...)
        if (methodName is "Sum" or "Min" or "Max" or "Average")
        {
            if (node.Arguments.Count == 2 && IsGroupingParameter(node.Arguments[0]))
            {
                var selectorLambda = ExtractLambda(node.Arguments[1]);
                if (selectorLambda != null)
                {
                    var member = _memberFor(selectorLambda.Body);
                    var sqlOp = methodName == "Average" ? "avg" : methodName.ToLowerInvariant();
                    return $"{sqlOp}({member.TypedLocator})";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a HAVING clause predicate from the grouping's Where expression.
    /// Returns the SQL for the predicate.
    /// </summary>
    public static ISqlFragment ResolveHavingFragment(
        Expression expression,
        IQueryableMemberCollection collection,
        LambdaExpression keySelector,
        Dictionary<string, IQueryableMember> keyMembers,
        IQueryableMember simpleKeyMember,
        bool isCompositeKey)
    {
        var resolver = new HavingExpressionResolver(collection, keySelector, keyMembers, simpleKeyMember, isCompositeKey);
        return resolver.Resolve(expression);
    }

    private bool IsGroupingKeyAccess(MemberExpression node)
    {
        return node.Member.Name == "Key"
               && node.Expression is ParameterExpression param
               && param == _groupingParameter;
    }

    private bool IsGroupingParameter(Expression node)
    {
        return node is ParameterExpression param && param == _groupingParameter;
    }

    private static LambdaExpression ExtractLambda(Expression expr)
    {
        if (expr is UnaryExpression unary)
        {
            expr = unary.Operand;
        }

        return expr as LambdaExpression;
    }

    private string ResolvePredicate(Expression expr)
    {
        var lambda = ExtractLambda(expr);
        if (lambda == null)
        {
            throw new BadLinqExpressionException("Expected a lambda predicate in GroupBy aggregate");
        }

        // Simple predicate support: x => x.Flag
        var member = _memberFor(lambda.Body);
        return $"{member.TypedLocator} = True";
    }
}

/// <summary>
/// Translates Where() expressions on IGrouping to SQL HAVING clauses.
/// Supports aggregate comparisons like g.Count() > 5, g.Sum(x => x.Number) >= 100.
/// </summary>
internal class HavingExpressionResolver
{
    private readonly IQueryableMemberCollection _collection;
    private readonly LambdaExpression _keySelector;
    private readonly Dictionary<string, IQueryableMember> _keyMembers;
    private readonly IQueryableMember _simpleKeyMember;
    private readonly bool _isCompositeKey;

    public HavingExpressionResolver(
        IQueryableMemberCollection collection,
        LambdaExpression keySelector,
        Dictionary<string, IQueryableMember> keyMembers,
        IQueryableMember simpleKeyMember,
        bool isCompositeKey)
    {
        _collection = collection;
        _keySelector = keySelector;
        _keyMembers = keyMembers;
        _simpleKeyMember = simpleKeyMember;
        _isCompositeKey = isCompositeKey;
    }

    public ISqlFragment Resolve(Expression expression)
    {
        if (expression is BinaryExpression binary)
        {
            return ResolveBinary(binary);
        }

        throw new BadLinqExpressionException(
            "Marten only supports binary comparison expressions in GroupBy HAVING clauses");
    }

    private ISqlFragment ResolveBinary(BinaryExpression binary)
    {
        // Handle AND/OR
        if (binary.NodeType == ExpressionType.AndAlso)
        {
            var left = Resolve(binary.Left);
            var right = Resolve(binary.Right);
            return new CompoundFragment("and", left, right);
        }

        if (binary.NodeType == ExpressionType.OrElse)
        {
            var left = Resolve(binary.Left);
            var right = Resolve(binary.Right);
            return new CompoundFragment("or", left, right);
        }

        var op = binary.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw new BadLinqExpressionException(
                $"Unsupported comparison operator '{binary.NodeType}' in GroupBy HAVING clause")
        };

        var leftOperand = ResolveOperand(binary.Left);
        var rightOperand = ResolveOperand(binary.Right);

        return new HavingComparisonFragment(leftOperand, op, rightOperand);
    }

    private HavingOperand ResolveOperand(Expression expr)
    {
        // Aggregate call: g.Count(), g.Sum(x => x.Number)
        if (expr is MethodCallExpression method)
        {
            return HavingOperand.ForSql(ResolveAggregateCall(method)
                                        ?? throw new BadLinqExpressionException(
                                            $"Unsupported method '{method.Method.Name}' in GroupBy HAVING clause"));
        }

        // Constant
        if (expr is ConstantExpression constant)
        {
            return HavingOperand.ForValue(constant.Value);
        }

        // Key access: g.Key
        if (expr is MemberExpression member && member.Member.Name == "Key")
        {
            if (_isCompositeKey)
            {
                throw new BadLinqExpressionException(
                    "Cannot use composite key directly in HAVING clause");
            }

            return HavingOperand.ForSql(_simpleKeyMember!.TypedLocator);
        }

        // Try to evaluate as constant
        if (expr.TryToParseConstant(out var c))
        {
            return HavingOperand.ForValue(c.Value);
        }

        throw new BadLinqExpressionException(
            $"Unsupported expression type '{expr.NodeType}' in GroupBy HAVING clause");
    }

    private string ResolveAggregateCall(MethodCallExpression node)
    {
        var methodName = node.Method.Name;

        if (methodName is "Count" or "LongCount")
        {
            return "count(*)";
        }

        if (methodName is "Sum" or "Min" or "Max" or "Average" && node.Arguments.Count >= 2)
        {
            var lambda = ExtractLambda(node.Arguments[1]);
            if (lambda != null)
            {
                var member = _collection.MemberFor(lambda.Body);
                var sqlOp = methodName == "Average" ? "avg" : methodName.ToLowerInvariant();
                return $"{sqlOp}({member.TypedLocator})";
            }
        }

        return null;
    }

    private static LambdaExpression ExtractLambda(Expression expr)
    {
        if (expr is UnaryExpression unary) expr = unary.Operand;
        return expr as LambdaExpression;
    }
}

/// <summary>
///     One side of a HAVING comparison: either SQL text Marten itself derived from the document model
///     (an aggregate call, or the group key's locator), or a runtime VALUE captured from the caller's
///     expression.
/// </summary>
/// <remarks>
///     GHSA-q4xm-rhx9-xjm4. The two used to be the same thing — a <c>string</c> — and a captured value
///     reached the SQL through <c>ToString()</c> and a raw <c>builder.Append</c>. Marten put no quotes
///     around it, so a string operand from untrusted input did not even need a quote to break out of:
///     it supplied the entire literal, and everything after it. Keeping the two cases apart in the type
///     is what makes the parameterized path the only path a value can take.
/// </remarks>
internal readonly struct HavingOperand
{
    private HavingOperand(string? sql, object? value, bool isValue)
    {
        Sql = sql;
        Value = value;
        IsValue = isValue;
    }

    internal string? Sql { get; }
    internal object? Value { get; }
    internal bool IsValue { get; }

    /// <summary>SQL Marten derived from the document model. Never caller text.</summary>
    internal static HavingOperand ForSql(string sql) => new(sql, null, false);

    /// <summary>A runtime value from the caller's expression. Always parameterized.</summary>
    internal static HavingOperand ForValue(object? value) => new(null, value, true);

    internal void Apply(ICommandBuilder builder)
    {
        if (!IsValue)
        {
            builder.Append(Sql!);
            return;
        }

        // A null constant cannot be parameterized into `= ?` and still mean anything, so it keeps
        // rendering as the NULL literal exactly as before. `NULL` is a keyword, not caller text.
        if (Value is null)
        {
            builder.Append("NULL");
            return;
        }

        builder.AppendParameter(Value);
    }
}

internal class HavingComparisonFragment: ISqlFragment
{
    private readonly HavingOperand _left;
    private readonly string _op;
    private readonly HavingOperand _right;

    public HavingComparisonFragment(HavingOperand left, string op, HavingOperand right)
    {
        _left = left;
        _op = op;
        _right = right;
    }

    public void Apply(ICommandBuilder builder)
    {
        _left.Apply(builder);
        builder.Append(" ");
        builder.Append(_op);
        builder.Append(" ");
        _right.Apply(builder);
    }
}

internal class CompoundFragment: ISqlFragment
{
    private readonly string _separator;
    private readonly ISqlFragment _left;
    private readonly ISqlFragment _right;

    public CompoundFragment(string separator, ISqlFragment left, ISqlFragment right)
    {
        _separator = separator;
        _left = left;
        _right = right;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("(");
        _left.Apply(builder);
        builder.Append($" {_separator} ");
        _right.Apply(builder);
        builder.Append(")");
    }
}
