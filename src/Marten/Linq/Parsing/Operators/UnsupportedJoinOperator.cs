#nullable enable
using System;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

/// <summary>
///     #5731. <c>Queryable.Join</c>, the <c>LeftJoin</c> / <c>RightJoin</c> pair .NET 10 added, the
///     <c>FullJoin</c> .NET 11 adds, and the result-selector-less overloads of all four that return
///     <c>(TOuter, TInner)</c> tuples. Marten translates none of them.
/// </summary>
/// <remarks>
///     <para>
///         They used to fall through to the generic "does not (yet) support Linq operator" refusal,
///         which is true but leaves the reader to discover that the join they wanted is already
///         there under another spelling: <c>GroupJoin()</c> + <c>SelectMany()</c> is Marten's inner
///         join, and adding <c>DefaultIfEmpty()</c> makes it the left join. So each operator is
///         refused here with the rewrite that answers it, and <c>FullJoin</c>, which has no
///         translation at all, says so.
///     </para>
///     <para>
///         Refusing is the whole job. This runs before any SQL is built, so nothing is half-applied
///         to the query.
///     </para>
/// </remarks>
internal class UnsupportedJoinOperator: LinqOperator
{
    internal const string GroupJoinDocs = "https://martendb.io/documents/querying/linq/group-join.html";

    public UnsupportedJoinOperator(string methodName): base(methodName)
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        throw new BadLinqExpressionException(MessageFor(expression));
    }

    internal static string MessageFor(MethodCallExpression expression)
    {
        var name = expression.Method.Name;
        var answer = name switch
        {
            "Join" =>
                "Write the inner join as GroupJoin() followed by SelectMany(), which Marten translates into a SQL INNER JOIN: " +
                "outer.GroupJoin(inner, o => o.Key, i => i.Key, (o, matches) => new { o, matches })" +
                ".SelectMany(x => x.matches, (x, i) => new { ... }).",

            "LeftJoin" =>
                "Write the left join as GroupJoin() followed by SelectMany() over DefaultIfEmpty(), which Marten translates into a SQL LEFT JOIN: " +
                "outer.GroupJoin(inner, o => o.Key, i => i.Key, (o, matches) => new { o, matches })" +
                ".SelectMany(x => x.matches.DefaultIfEmpty(), (x, i) => new { ... }).",

            "RightJoin" =>
                "Swap the two sources and write it as a left join: GroupJoin() followed by SelectMany() over DefaultIfEmpty(), " +
                "starting from the side whose rows must all be kept.",

            "FullJoin" =>
                "Marten has no FULL OUTER JOIN translation. Query the two sides separately and combine them in memory, " +
                "or write the SQL yourself with session.QueryAsync<T>(sql).",

            _ => throw new ArgumentOutOfRangeException(nameof(expression), name, "Not a join operator")
        };

        var tuple = returnsTuples(expression)
            ? " The overload without a result selector returns (outer, inner) tuples; with GroupJoin() the shape of each row is " +
              "named by the SelectMany() result selector instead, such as new { Outer = x.o, Inner = i }."
            : string.Empty;

        return $"Marten does not translate Queryable.{name}(). {answer}{tuple} See {GroupJoinDocs}";
    }

    // Queryable's join overloads are (outer, inner, outerKey, innerKey[, resultSelector][, comparer]),
    // so the fifth argument is a quoted lambda exactly when there is a result selector.
    private static bool returnsTuples(MethodCallExpression expression)
    {
        return expression.Arguments.Count < 5 || expression.Arguments[4].UnBox() is not LambdaExpression;
    }
}
