#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Marten.Linq.Parsing;

namespace Marten.Linq.Caching;

/// <summary>
///     Walks a LINQ expression tree (the Where/OrderBy/Skip/Take/Select chain built by
///     <see cref="MartenLinqQueryable{T}" />) to compute a structural "shape" key that is
///     stable across calls with the same query shape but different captured (closure)
///     values, and collects the ordered list of value-bearing "slot" sub-expressions --
///     the leaves that vary between calls (constants, and closure-captured member
///     accesses).
/// </summary>
/// <remarks>
///     This is deliberately conservative. Anything that isn't one of a small allow-listed
///     set of node types (or a non-allow-listed method call such as <c>StartsWith()</c>,
///     <c>Include()</c>, <c>Stats()</c>, <c>GroupBy()</c>, <c>SelectMany()</c>, custom SQL,
///     etc.) flips <see cref="IsSupported" /> to <c>false</c>, which means the query plan
///     cache (<see cref="QueryPlanCache" />) will never attempt to cache it. Correctness
///     always wins over caching a shape we can't fully reason about.
/// </remarks>
internal sealed class ExpressionShapeVisitor: ExpressionVisitor
{
    private static readonly HashSet<string> AllowedQueryableMethods = new()
    {
        nameof(Queryable.Where),
        nameof(Queryable.OrderBy),
        nameof(Queryable.OrderByDescending),
        nameof(Queryable.ThenBy),
        nameof(Queryable.ThenByDescending),
        nameof(Queryable.Skip),
        nameof(Queryable.Take),
        nameof(Queryable.Select)
    };

    private readonly StringBuilder _shape = new();
    private readonly List<Expression> _slots = new();
    private bool _supported = true;

    private ExpressionShapeVisitor()
    {
    }

    /// <summary>
    ///     False if the expression uses anything outside of the narrow MVP scope (simple
    ///     Where comparisons, OrderBy/ThenBy, Skip/Take, Select of members) that the plan
    ///     cache doesn't know how to safely replay.
    /// </summary>
    public bool IsSupported => _supported;

    /// <summary>
    ///     The ordered list of sub-expressions -- closures and literal constants -- whose
    ///     runtime values may differ between calls sharing this shape.
    /// </summary>
    public IReadOnlyList<Expression> Slots => _slots;

    public static ExpressionShapeVisitor Analyze(Expression expression)
    {
        var visitor = new ExpressionShapeVisitor();

        try
        {
            visitor.Visit(expression);
        }
        catch (Exception)
        {
            // Any failure while walking the tree means we don't understand this shape
            // well enough to cache it. Fall back to the always-correct, uncached path.
            visitor._supported = false;
        }

        return visitor;
    }

    /// <summary>
    ///     Builds a stable cache key for this shape, scoped to the source document type and
    ///     the requested result type (so two structurally identical shapes over different
    ///     document types never collide).
    /// </summary>
    public string BuildKey(Type sourceType, Type resultType)
    {
        var text = sourceType.FullName + "|" + resultType.FullName + "|" + _shape;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    public override Expression? Visit(Expression? node)
    {
        if (node == null)
        {
            return null;
        }

        if (!_supported)
        {
            return node;
        }

        switch (node.NodeType)
        {
            case ExpressionType.Call:
            case ExpressionType.Lambda:
            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
            case ExpressionType.AndAlso:
            case ExpressionType.OrElse:
            case ExpressionType.And:
            case ExpressionType.Or:
            case ExpressionType.Convert:
            case ExpressionType.ConvertChecked:
            case ExpressionType.Not:
            case ExpressionType.Negate:
            case ExpressionType.Quote:
            case ExpressionType.MemberAccess:
            case ExpressionType.Constant:
            case ExpressionType.Parameter:
            case ExpressionType.New:
                return base.Visit(node);
            default:
                // Anything else (conditional, indexers, member init, invoke, etc.) is out
                // of scope for the MVP cache.
                _supported = false;
                return node;
        }
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if ((node.Method.DeclaringType == typeof(Queryable) || node.Method.DeclaringType == typeof(Enumerable))
            && AllowedQueryableMethods.Contains(node.Method.Name))
        {
            _shape.Append("M(").Append(node.Method.Name);
            foreach (var t in node.Method.GetGenericArguments())
            {
                _shape.Append('<').Append(t.FullName).Append('>');
            }

            _shape.Append(':');
            foreach (var argument in node.Arguments)
            {
                Visit(argument);
                _shape.Append(',');
            }

            _shape.Append(')');
            return node;
        }

        // Anything else -- Contains(), StartsWith(), Include(), Stats(), custom SQL,
        // GroupBy, SelectMany, etc. -- is out of scope for the MVP cache.
        _supported = false;
        return node;
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        _shape.Append("λ(");
        Visit(node.Body);
        _shape.Append(')');
        return node;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        _shape.Append(node.NodeType).Append('(');
        Visit(node.Left);
        _shape.Append(',');
        Visit(node.Right);
        _shape.Append(')');
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        _shape.Append(node.NodeType).Append('(');
        Visit(node.Operand);
        _shape.Append(')');
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.IsCompilableExpression())
        {
            RecordSlot(node);
            return node;
        }

        _shape.Append("Mem(").Append(node.Member.DeclaringType?.FullName).Append('.').Append(node.Member.Name)
            .Append(':');
        Visit(node.Expression);
        _shape.Append(')');
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value is IQueryable)
        {
            // The root document collection anchor -- structurally stable across every
            // call with this shape, not a value slot.
            _shape.Append("Root(").Append(node.Type.FullName).Append(')');
            return node;
        }

        RecordSlot(node);
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _shape.Append("Param(").Append(node.Type.FullName).Append(')');
        return node;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        _shape.Append("New(").Append(node.Type.FullName).Append(':');
        foreach (var argument in node.Arguments)
        {
            Visit(argument);
            _shape.Append(',');
        }

        _shape.Append(')');
        return node;
    }

    private void RecordSlot(Expression node)
    {
        _shape.Append("Slot").Append(_slots.Count).Append(':').Append(node.Type.FullName);
        _slots.Add(node);
    }
}

