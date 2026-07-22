#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Marten.Schema;

namespace Marten.Linq.CursorPaging;

/// <summary>
/// A single OrderBy/OrderByDescending/ThenBy/ThenByDescending clause parsed out of
/// a Linq expression tree, in the order it was originally applied.
/// </summary>
internal sealed class CursorOrdering
{
    public CursorOrdering(LambdaExpression selector, bool descending)
    {
        Selector = selector;
        Descending = descending;
    }

    public LambdaExpression Selector { get; }
    public bool Descending { get; }

    /// <summary>
    /// The CLR type produced by this ordering's key selector.
    /// </summary>
    public Type KeyType => Selector.ReturnType;

    /// <summary>
    /// The member (property/field) directly accessed by the key selector, if the
    /// selector body is a simple member access (e.g. <c>x =&gt; x.Id</c>). Used to
    /// validate the terminal ordering is unique.
    /// </summary>
    public MemberInfo? Member => (StripConvert(Selector.Body) as MemberExpression)?.Member;

    private Delegate? _boxedAccessor;

    /// <summary>
    /// Extract this ordering's key value (boxed) from a materialized document instance.
    /// The boxing delegate is compiled once and cached on first use.
    /// </summary>
    public object? GetValue(object document)
    {
        _boxedAccessor ??= BuildBoxedAccessor();
        return _boxedAccessor.DynamicInvoke(document);
    }

    private Delegate BuildBoxedAccessor()
    {
        var parameter = Selector.Parameters[0];
        var body = Expression.Convert(Selector.Body, typeof(object));
        return Expression.Lambda(body, parameter).Compile();
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }
}

/// <summary>
/// Parses the OrderBy/ThenBy chain out of a Marten Linq expression tree and builds
/// the "seek" (a.k.a. keyset) <c>Where</c> predicate used to fetch the next page of
/// results for <see cref="CursorPagingQueryableExtensions.ToJsonPageByCursorAsync{T}"/>.
/// </summary>
internal static class CursorPagination
{
    private const string CursorPrefix = "v1:";

    /// <summary>
    /// Walk the expression tree looking for a contiguous OrderBy/ThenBy chain and
    /// return the orderings in the order they were originally declared.
    /// </summary>
    public static IReadOnlyList<CursorOrdering> ParseOrderings(Expression expression)
    {
        var orderings = new List<CursorOrdering>();

        var current = expression;
        while (current is MethodCallExpression { Method.DeclaringType: var declaringType } call
               && declaringType == typeof(Queryable)
               && call.Arguments.Count == 2
               && IsOrderingMethod(call.Method.Name, out var descending))
        {
            var selector = (LambdaExpression)StripQuote(call.Arguments[1]);
            orderings.Add(new CursorOrdering(selector, descending));

            current = call.Arguments[0];

            // OrderBy/OrderByDescending marks the start of the ordering chain;
            // ThenBy/ThenByDescending can only be nested on top of it, so once we
            // hit the root OrderBy call we're done collecting.
            if (call.Method.Name is "OrderBy" or "OrderByDescending")
            {
                break;
            }
        }

        orderings.Reverse();
        return orderings;
    }

    private static bool IsOrderingMethod(string name, out bool descending)
    {
        switch (name)
        {
            case "OrderBy":
            case "ThenBy":
                descending = false;
                return true;
            case "OrderByDescending":
            case "ThenByDescending":
                descending = true;
                return true;
            default:
                descending = false;
                return false;
        }
    }

    private static Expression StripQuote(Expression expression)
    {
        return expression is UnaryExpression { NodeType: ExpressionType.Quote } quote
            ? quote.Operand
            : expression;
    }

    /// <summary>
    /// Guards against non-deterministic pagination: the last ordering in the chain
    /// must be guaranteed unique (by default, the document's identity member) or
    /// seek pagination can skip or repeat rows that share the same leading sort keys.
    /// </summary>
    public static void ValidateTerminalKeyIsUnique<T>(CursorOrdering terminal)
    {
        var idMember = DocumentMapping.FindIdMember(typeof(T));

        if (idMember != null && terminal.Member != null && MemberNamesMatch(terminal.Member, idMember))
        {
            return;
        }

        throw new InvalidOperationException(
            $"StreamPagedByCursor<{typeof(T).Name}> requires the final OrderBy/ThenBy clause to be on a " +
            $"member guaranteed to be unique across the result set (typically the document identity, " +
            $"'{idMember?.Name ?? "Id"}'). Add '.ThenBy(x => x.{idMember?.Name ?? "Id"})' as the last " +
            "ordering clause to make the sort deterministic for keyset pagination.");
    }

    private static bool MemberNamesMatch(MemberInfo a, MemberInfo b)
    {
        return string.Equals(a.Name, b.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Build the composite "seek" predicate:
    /// <c>(k1 &gt; v1) OR (k1 == v1 AND k2 &gt; v2) OR ... OR (k1==v1 AND ... AND kn &gt; vn)</c>
    /// flipping &gt;/&lt; per ordering direction, given the cursor's decoded boundary values.
    /// </summary>
    public static Expression<Func<T, bool>> BuildSeekPredicate<T>(IReadOnlyList<CursorOrdering> orderings, object?[] values)
    {
        var parameter = Expression.Parameter(typeof(T), "x");

        Expression? orExpression = null;

        for (var i = 0; i < orderings.Count; i++)
        {
            Expression? andExpression = null;

            for (var j = 0; j < i; j++)
            {
                var equals = BuildComparison(orderings[j], values[j], parameter, ComparisonKind.Equal);
                andExpression = andExpression == null ? equals : Expression.AndAlso(andExpression, equals);
            }

            var comparison = BuildComparison(orderings[i], values[i], parameter,
                orderings[i].Descending ? ComparisonKind.LessThan : ComparisonKind.GreaterThan);

            andExpression = andExpression == null ? comparison : Expression.AndAlso(andExpression, comparison);

            orExpression = orExpression == null ? andExpression : Expression.OrElse(orExpression, andExpression);
        }

        return Expression.Lambda<Func<T, bool>>(orExpression!, parameter);
    }

    private enum ComparisonKind
    {
        Equal,
        GreaterThan,
        LessThan
    }

    private static Expression BuildComparison(CursorOrdering ordering, object? value, ParameterExpression parameter,
        ComparisonKind kind)
    {
        var left = new ReplaceParameterVisitor(ordering.Selector.Parameters[0], parameter).Visit(ordering.Selector.Body)!;
        var right = Expression.Constant(value, left.Type);

        if (kind == ComparisonKind.Equal)
        {
            // Equality is well-defined for every type via Expression.Equal (it falls back to Object.Equals
            // for reference types that don't overload ==), unlike GreaterThan/LessThan.
            return Expression.Equal(left, right);
        }

        // Expression.GreaterThan/LessThan only work for numeric primitives and types with overloaded
        // comparison operators, which excludes ordinary types like string, Guid, or DateTimeOffset.
        // Marten's own Linq provider already knows how to translate `x.CompareTo(y) > 0`/`< 0` into SQL
        // comparisons (see Marten.Linq.Parsing.CompareToComparable), so route every non-equal comparison
        // through that same shape instead of the raw binary operator.
        var compareToMethod = left.Type.GetMethod("CompareTo", new[] { left.Type })
            ?? left.Type.GetMethod("CompareTo", new[] { typeof(object) });

        if (compareToMethod == null)
        {
            // Fall back to the raw operator for the rare key type with neither an overloaded operator
            // nor an IComparable(<T>) implementation - this will throw a clear .NET exception if unsupported.
            return kind == ComparisonKind.GreaterThan
                ? Expression.GreaterThan(left, right)
                : Expression.LessThan(left, right);
        }

        var compareArgument = compareToMethod.GetParameters()[0].ParameterType == typeof(object)
            ? Expression.Convert(right, typeof(object))
            : (Expression)right;

        var compareCall = Expression.Call(left, compareToMethod, compareArgument);

        return kind switch
        {
            ComparisonKind.GreaterThan => Expression.GreaterThan(compareCall, Expression.Constant(0)),
            ComparisonKind.LessThan => Expression.LessThan(compareCall, Expression.Constant(0)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    /// <summary>
    /// Base64-encode (with a leading version marker) the boxed sort key values of
    /// the last row in a page so they can be round-tripped back as the next
    /// request's cursor.
    /// </summary>
    public static string EncodeCursor(object?[] values)
    {
        var json = JsonSerializer.Serialize(values);
        var bytes = Encoding.UTF8.GetBytes(json);
        return CursorPrefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decode a cursor string produced by <see cref="EncodeCursor"/>, converting
    /// each element back to the CLR type produced by its matching ordering's key
    /// selector.
    /// </summary>
    public static object?[] DecodeCursor(string cursor, IReadOnlyList<CursorOrdering> orderings)
    {
        if (!cursor.StartsWith(CursorPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unrecognized cursor format.", nameof(cursor));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cursor[CursorPrefix.Length..]);
        }
        catch (FormatException e)
        {
            throw new ArgumentException("Invalid cursor.", nameof(cursor), e);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException e)
        {
            throw new ArgumentException("Invalid cursor.", nameof(cursor), e);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != orderings.Count)
            {
                throw new ArgumentException(
                    "The supplied cursor does not match the ordering of the supplied queryable.", nameof(cursor));
            }

            var values = new object?[orderings.Count];
            for (var i = 0; i < orderings.Count; i++)
            {
                values[i] = JsonSerializer.Deserialize(root[i].GetRawText(), orderings[i].KeyType);
            }

            return values;
        }
    }

    private sealed class ReplaceParameterVisitor: ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _from ? _to : base.VisitParameter(node);
        }
    }
}
