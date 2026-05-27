#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

internal class DistinctByOperator: LinqOperator
{
    public DistinctByOperator(): base("DistinctBy")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        // DistinctBy(this IQueryable<T> source, Expression<Func<T, TKey>> keySelector)
        // translates to PostgreSQL `SELECT DISTINCT ON (key) ...`. The key selector is
        // resolved during compilation the same way an OrderBy member is, which also lets
        // us satisfy Postgres's rule that the DISTINCT ON expression be the leftmost
        // ORDER BY expression. See https://github.com/JasperFx/marten/issues/4565.
        var usage = query.CollectionUsageFor(expression);
        usage.DistinctByExpression = expression.Arguments[1];
    }
}
