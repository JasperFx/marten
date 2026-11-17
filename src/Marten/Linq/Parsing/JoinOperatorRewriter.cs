#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Parsing.Operators;

namespace Marten.Linq.Parsing;

/// <summary>
///     #5910. Rewrites <c>Queryable.Join</c>, and the <c>LeftJoin</c> / <c>RightJoin</c> pair .NET 10
///     added, into the <c>GroupJoin()</c> + <c>SelectMany()</c> (+ <c>DefaultIfEmpty()</c>) form that
///     Marten already translates, before the expression is parsed.
/// </summary>
/// <remarks>
///     <para>
///         The rewrite produces exactly the tree a user would write by hand:
///         <c>outer.Join(inner, ok, ik, (o, i) =&gt; r)</c> becomes
///         <c>outer.GroupJoin(inner, ok, ik, (o, g) =&gt; new JoinedRows(o, g)).SelectMany(x =&gt; x.Inner, (x, i) =&gt; r[o := x.Outer])</c>,
///         and <c>LeftJoin</c> the same over <c>x.Inner.DefaultIfEmpty()</c>. <c>RightJoin</c> is the left
///         join with the two sources (and key selectors) swapped, and its result selector re-bound so
///         that its outer parameter is the <c>SelectMany</c> element and its inner one <c>x.Outer</c>. So
///         every operator after the join, and the SQL, is whatever the hand-written form gets.
///     </para>
///     <para>
///         The .NET 11 overloads without a result selector, which return <c>(TOuter, TInner)</c>
///         tuples, get <c>(o, i) =&gt; new ValueTuple&lt;TOuter, TInner&gt;(o, i)</c> as theirs.
///     </para>
///     <para>
///         Only the shapes the GroupJoin path is known to answer correctly are rewritten. Everything
///         else is refused here with <see cref="BadLinqExpressionException" />, because the GroupJoin
///         path would otherwise drop what it does not understand and return plausible but wrong rows:
///         an operator other than <c>Where()</c> before the join on either source (an ordering a SQL
///         join does not keep, a <c>Take()</c> or <c>Skip()</c> that would apply to the wrong rows, a
///         projection), an inner source that is not a Marten <c>Query&lt;T&gt;()</c>, a composite or
///         computed key, an <c>IEqualityComparer</c>, and a join of a join. <c>FullJoin</c> is not
///         touched; the operator library still refuses it.
///     </para>
///     <para>
///         Only the operator chain itself (each call's source argument) is walked, so this costs a
///         handful of type checks on a query without a join.
///     </para>
/// </remarks>
internal static class JoinOperatorRewriter
{
    private static readonly MethodInfo _groupJoinDefinition =
        new Func<IQueryable<object>, IEnumerable<object>, Expression<Func<object, object>>,
                Expression<Func<object, object>>, Expression<Func<object, IEnumerable<object>, object>>,
                IQueryable<object>>(Queryable.GroupJoin)
            .Method.GetGenericMethodDefinition();

    private static readonly MethodInfo _selectManyDefinition =
        new Func<IQueryable<object>, Expression<Func<object, IEnumerable<object>>>,
                Expression<Func<object, object, object>>, IQueryable<object>>(Queryable.SelectMany)
            .Method.GetGenericMethodDefinition();

    private static readonly MethodInfo _defaultIfEmptyDefinition =
        new Func<IEnumerable<object>, IEnumerable<object?>>(Enumerable.DefaultIfEmpty)
            .Method.GetGenericMethodDefinition();

    private enum JoinKind
    {
        Inner,
        Left,
        Right
    }

    /// <summary>
    ///     Returns <paramref name="expression" /> with every <c>Join</c>, <c>LeftJoin</c> and
    ///     <c>RightJoin</c> on its operator chain rewritten, or the same instance when there is none.
    /// </summary>
    public static Expression Rewrite(Expression expression)
    {
        if (expression is not MethodCallExpression call || call.Arguments.Count == 0)
        {
            return expression;
        }

        var source = call.Arguments[0];
        var rewrittenSource = Rewrite(source);

        if (call.Method.DeclaringType == typeof(Queryable))
        {
            switch (call.Method.Name)
            {
                case "Join":
                    return rewriteJoin(call, rewrittenSource, JoinKind.Inner);
                case "LeftJoin":
                    return rewriteJoin(call, rewrittenSource, JoinKind.Left);
                case "RightJoin":
                    return rewriteJoin(call, rewrittenSource, JoinKind.Right);
            }
        }

        if (ReferenceEquals(rewrittenSource, source))
        {
            return call;
        }

        // A hand-written GroupJoin over a join this rewrote: one join per query, as for Join over Join
        if (call.Method.DeclaringType == typeof(Queryable) && call.Method.Name == nameof(Queryable.GroupJoin))
        {
            throw refusal(call.Method.Name, joinOfAJoin);
        }

        var arguments = call.Arguments.ToArray();
        arguments[0] = rewrittenSource;
        return call.Update(call.Object, arguments);
    }

    private static Expression rewriteJoin(MethodCallExpression call, Expression outerSource, JoinKind kind)
    {
        var name = call.Method.Name;

        // (outer, inner, outerKey, innerKey[, resultSelector][, comparer]). Without a result selector this
        // is one of the .NET 11 overloads that return (outer, inner) tuples, which take the comparer as an
        // optional parameter -- so a call without one still carries the argument, as a null constant.
        var hasResultSelector = call.Arguments.Count >= 5 && call.Arguments[4].UnBox() is LambdaExpression;
        var comparerIndex = hasResultSelector ? 5 : 4;
        var hasComparer = call.Arguments.Count > comparerIndex
                          && call.Arguments[comparerIndex] is not ConstantExpression { Value: null };
        if (hasComparer)
        {
            throw refusal(name,
                "the overload that takes an IEqualityComparer cannot be translated to SQL, which compares the stored keys with '='. " +
                "Drop the comparer");
        }

        var genericArguments = call.Method.GetGenericArguments();
        var outerType = genericArguments[0];
        var innerType = genericArguments[1];
        var keyType = genericArguments[2];

        // IQueryable<TResult>, or IQueryable<(TOuter Outer, TInner Inner)> for a tuple overload
        var resultType = call.Type.GetGenericArguments()[0];

        var resultSelector = hasResultSelector
            ? (LambdaExpression)call.Arguments[4].UnBox()
            : tupleSelector(outerType, innerType, resultType);

        var innerSource = call.Arguments[1];
        var outerKey = (LambdaExpression)call.Arguments[2].UnBox();
        var innerKey = (LambdaExpression)call.Arguments[3].UnBox();

        assertKeyIsMember(name, outerKey, "outer");
        assertKeyIsMember(name, innerKey, "inner");

        if (kind == JoinKind.Right)
        {
            // The side whose rows must all be kept becomes the GroupJoin's outer source, so both
            // sources become plain Query<T>().Where(...) chains: the old outer is now the inner.
            assertIsDocumentQuery(name, outerSource, "outer");
            assertIsDocumentQuery(name, innerSource, "inner");

            return buildGroupJoin(
                groupOuter: innerSource, groupInner: outerSource,
                groupOuterType: innerType, groupInnerType: outerType, keyType: keyType, resultType: resultType,
                groupOuterKey: innerKey, groupInnerKey: outerKey,
                isLeftJoin: true,
                // (o, i) => r: o is the element the SelectMany flattens, i is x.Outer
                elementParameter: resultSelector.Parameters[0],
                outerParameter: resultSelector.Parameters[1],
                resultBody: resultSelector.Body);
        }

        assertOnlyFilteredBeforeJoin(name, outerSource);
        assertIsDocumentQuery(name, innerSource, "inner");

        return buildGroupJoin(
            groupOuter: outerSource, groupInner: innerSource,
            groupOuterType: outerType, groupInnerType: innerType, keyType: keyType, resultType: resultType,
            groupOuterKey: outerKey, groupInnerKey: innerKey,
            isLeftJoin: kind == JoinKind.Left,
            elementParameter: resultSelector.Parameters[1],
            outerParameter: resultSelector.Parameters[0],
            resultBody: resultSelector.Body);
    }

    // (o, i) => new ValueTuple<TOuter, TInner>(o, i): what the .NET 11 tuple overloads return, written as
    // the result selector the GroupJoin path already projects. The element names (Outer, Inner) are
    // compile-time only, so the tuple is the plain ValueTuple<TOuter, TInner>.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "The constructor of the ValueTuple<,> that the query's own element type already is.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Expression.Lambda closes Func<,,> over the join's document types for an expression tree that is translated to SQL and never compiled.")]
    private static LambdaExpression tupleSelector(Type outerType, Type innerType, Type tupleType)
    {
        var outer = Expression.Parameter(outerType, "outer");
        var inner = Expression.Parameter(innerType, "inner");
        var constructor = tupleType.GetConstructor([outerType, innerType])!;

        return Expression.Lambda(Expression.New(constructor, outer, inner), outer, inner);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "MakeGenericMethod / MakeGenericType / Expression.Lambda close Queryable.GroupJoin, Queryable.SelectMany, Enumerable.DefaultIfEmpty and JoinedRows<,> over the join's own document types. They are only named in an expression tree that Marten translates to SQL; nothing here is ever invoked or compiled.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "MakeGenericMethod over Queryable/Enumerable operators that the expression tree only names.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "JoinedRows<,> is Marten's own type, and its constructor and two properties are referenced statically by nameof() and by the record itself.")]
    private static Expression buildGroupJoin(
        Expression groupOuter, Expression groupInner,
        Type groupOuterType, Type groupInnerType, Type keyType, Type resultType,
        LambdaExpression groupOuterKey, LambdaExpression groupInnerKey,
        bool isLeftJoin,
        ParameterExpression elementParameter, ParameterExpression outerParameter, Expression resultBody)
    {
        var rowType = typeof(JoinedRows<,>).MakeGenericType(groupOuterType, groupInnerType);
        var groupType = typeof(IEnumerable<>).MakeGenericType(groupInnerType);
        var rowConstructor = rowType.GetConstructor([groupOuterType, groupType])!;
        var outerProperty = rowType.GetProperty(nameof(JoinedRows<object, object>.Outer))!;
        var innerProperty = rowType.GetProperty(nameof(JoinedRows<object, object>.Inner))!;

        // (o, g) => new JoinedRows<TOuter, TInner>(o, g) -- the hand-written (o, g) => new { o, g }. The
        // constructor's parameter names are the property names, which is how JoinSelectParser tells the
        // two sides apart in the SelectMany result selector.
        var o = Expression.Parameter(groupOuterType, "outer");
        var g = Expression.Parameter(groupType, "matches");
        var groupResultSelector = Expression.Lambda(
            Expression.New(rowConstructor, o, g),
            o, g);

        var groupJoin = Expression.Call(
            _groupJoinDefinition.MakeGenericMethod(groupOuterType, groupInnerType, keyType, rowType),
            groupOuter,
            groupInner,
            Expression.Quote(groupOuterKey),
            Expression.Quote(groupInnerKey),
            Expression.Quote(groupResultSelector));

        // x => x.Inner, or x => x.Inner.DefaultIfEmpty() for a left join
        var x = Expression.Parameter(rowType, "x");
        Expression matches = Expression.Property(x, innerProperty);
        if (isLeftJoin)
        {
            matches = Expression.Call(_defaultIfEmptyDefinition.MakeGenericMethod(groupInnerType), matches);
        }

        var collectionSelector = Expression.Lambda(matches, x);

        // (x, i) => r with the result selector's outer parameter re-bound to x.Outer. The inner one is
        // reused as the SelectMany element parameter, so the body is otherwise the user's own.
        var body = new ParameterToExpressionVisitor(outerParameter, Expression.Property(x, outerProperty)).Visit(resultBody);
        var flattenedSelector = Expression.Lambda(body, x, elementParameter);

        return Expression.Call(
            _selectManyDefinition.MakeGenericMethod(rowType, groupInnerType, resultType),
            groupJoin,
            Expression.Quote(collectionSelector),
            Expression.Quote(flattenedSelector));
    }

    // Anything before the join on the outer source lands in the outer CTE. Where() is the one operator
    // that means the same thing there. An ordering is not kept by a SQL join, and Take()/Skip()/Distinct()
    // or a projection either change which rows join or are not what the GroupJoin path reads.
    private static void assertOnlyFilteredBeforeJoin(string name, Expression outerSource)
    {
        var current = outerSource;
        while (current is MethodCallExpression call)
        {
            assertNotAJoinOfAJoin(name, call);

            if (!isPlainWhere(call))
            {
                throw refusal(name,
                    $"only Where() may come before it on the outer source, and this query applies {call.Method.Name}() first. " +
                    "Apply OrderBy(), Take(), Skip(), Select() and the like after the join instead, where they apply to the joined rows");
            }

            current = call.Arguments[0];
        }
    }

    private static void assertIsDocumentQuery(string name, Expression source, string side)
    {
        var current = source;
        while (current is MethodCallExpression call)
        {
            assertNotAJoinOfAJoin(name, call);

            if (!isPlainWhere(call))
            {
                throw refusal(name,
                    $"the {side} source may only be session.Query<T>() filtered by Where(), and it applies {call.Method.Name}(). " +
                    "Move that operator after the join");
            }

            current = call.Arguments[0];
        }

        // The root has to be the Query<T>() itself. An in-memory collection would be read as the whole
        // document table of its element type, and a Marten queryable passed through AsEnumerable() is a
        // constant whose own operators are invisible from here.
        if (current is ConstantExpression { Value: IMartenLinqQueryable queryable } constant
            && queryable.Expression is ConstantExpression root && ReferenceEquals(root.Value, constant.Value))
        {
            return;
        }

        throw refusal(name,
            $"the {side} source must be a Marten session.Query<T>(), optionally filtered by Where(). " +
            "Joining to an in-memory collection, a compiled query's parameter, or a queryable passed through AsEnumerable() is not translated");
    }

    private const string joinOfAJoin =
        "one of its sources is itself a join, and Marten translates one join per query. " +
        "Query the first join and combine it with the third source in memory, or use session.QueryAsync<T>(sql)";

    private static void assertNotAJoinOfAJoin(string name, MethodCallExpression call)
    {
        // A join rewritten further down the chain shows up here as its SelectMany / GroupJoin
        if (call.Method.Name is nameof(Queryable.SelectMany) or nameof(Queryable.GroupJoin))
        {
            throw refusal(name, joinOfAJoin);
        }
    }

    private static bool isPlainWhere(MethodCallExpression call)
    {
        return call.Method.DeclaringType == typeof(Queryable)
               && call.Method.Name == nameof(Queryable.Where)
               && call.Arguments.Count == 2
               && call.Arguments[1].UnBox() is LambdaExpression { Parameters.Count: 1 };
    }

    private static void assertKeyIsMember(string name, LambdaExpression keySelector, string side)
    {
        var body = keySelector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        if (body is NewExpression or MemberInitExpression)
        {
            throw refusal(name,
                $"the {side} key selector builds a composite key, and only a single member is translated. " +
                "Join on one member and filter the rest with Where() after the join");
        }

        var current = body;
        while (current is MemberExpression member)
        {
            current = member.Expression;
        }

        if (body is not MemberExpression || current != keySelector.Parameters[0])
        {
            throw refusal(name,
                $"the {side} key selector must be a member of the document, such as x => x.CustomerId, and it is {keySelector.Body}");
        }
    }

    private sealed class ParameterToExpressionVisitor: ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly Expression _to;

        public ParameterToExpressionVisitor(ParameterExpression from, Expression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _from ? _to : base.VisitParameter(node);
        }
    }

    private static BadLinqExpressionException refusal(string name, string reason)
    {
        return new BadLinqExpressionException(
            $"Marten cannot translate this Queryable.{name}(): {reason}. See {UnsupportedJoinOperator.GroupJoinDocs}");
    }
}

/// <summary>
///     #5910. The GroupJoin result a rewritten <c>Join</c> / <c>LeftJoin</c> / <c>RightJoin</c> carries
///     between its <c>GroupJoin()</c> and <c>SelectMany()</c>: the hand-written <c>new { o, g }</c>. Its
///     constructor's parameter names must stay the same as its property names.
/// </summary>
internal sealed record JoinedRows<TOuter, TInner>(TOuter Outer, IEnumerable<TInner> Inner);
