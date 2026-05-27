#nullable enable
using System.Linq.Expressions;
using Marten.Exceptions;

namespace Marten.Linq.Parsing.Operators;

internal class DistinctByOperator: LinqOperator
{
    public DistinctByOperator(): base("DistinctBy")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        // Marten does not (yet) translate DistinctBy() to SQL (it would map to
        // PostgreSQL's `SELECT DISTINCT ON (key) ...`). Rather than letting it fall
        // through to the generic "operator not supported" message, give callers the
        // concrete workaround. Tracked by https://github.com/JasperFx/marten/issues/4565.
        throw new BadLinqExpressionException(
            "Marten cannot translate the LINQ 'DistinctBy()' operator to SQL. Either use a scalar "
            + "'Distinct()' (for example 'Select(x => x.Foo).Distinct()'), or materialize the query "
            + "first with 'ToListAsync()' / 'ToList()' and then call 'DistinctBy()' in memory. Native "
            + "server-side DistinctBy translation is tracked by "
            + "https://github.com/JasperFx/marten/issues/4565.");
    }
}
