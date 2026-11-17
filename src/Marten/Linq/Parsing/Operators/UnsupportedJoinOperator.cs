#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

/// <summary>
///     #5731 / #5910. <c>Queryable.FullJoin</c>, which .NET 11 adds, along with its result-selector-less
///     overload that returns <c>(TOuter, TInner)</c> tuples. Marten has no FULL OUTER JOIN translation.
/// </summary>
/// <remarks>
///     <para>
///         <c>Join</c>, <c>LeftJoin</c> and <c>RightJoin</c> used to be refused here as well. They are now
///         rewritten into <c>GroupJoin()</c> + <c>SelectMany()</c> by <see cref="JoinOperatorRewriter" />
///         before the expression is parsed, which also refuses the shapes of them it cannot translate, so
///         they never reach the operator library. A full join has no such rewrite: neither side's rows
///         may be dropped, and the GroupJoin path can only keep all of one side.
///     </para>
///     <para>
///         Refusing is the whole job. This runs before any SQL is built, so nothing is half-applied to
///         the query.
///     </para>
/// </remarks>
internal class UnsupportedJoinOperator: LinqOperator
{
    internal const string GroupJoinDocs = "https://martendb.io/documents/querying/linq/group-join.html";

    public UnsupportedJoinOperator(): base("FullJoin")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        throw new BadLinqExpressionException(
            $"Marten does not translate Queryable.{expression.Method.Name}(). Marten has no FULL OUTER JOIN translation. " +
            "Query the two sides separately and combine them in memory, or write the SQL yourself with " +
            $"session.QueryAsync<T>(sql). Join(), LeftJoin() and RightJoin() are translated. See {GroupJoinDocs}");
    }
}
