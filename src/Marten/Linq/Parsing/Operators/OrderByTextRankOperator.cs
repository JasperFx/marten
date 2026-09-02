#nullable enable
using System.Linq.Expressions;
using Marten.Schema;
using Marten.Storage;
using Marten.Schema.Indexing.FullText;

namespace Marten.Linq.Parsing.Operators;

/// <summary>
///     Parses <c>OrderByTextRank()</c> / <c>ThenByTextRank()</c> into a <c>ts_rank</c> ordering (#5298).
/// </summary>
/// <remarks>
///     The pieces are captured here and resolved during <c>BuildFragment</c>, the same deferral
///     <see cref="OrderByNgramRankOperator" /> uses — the document mapping and the member collection are
///     not available at parse time.
/// </remarks>
internal class OrderByTextRankOperator: LinqOperator
{
    private readonly bool _then;

    public OrderByTextRankOperator(): this(nameof(QueryableExtensions.OrderByTextRank), false)
    {
    }

    protected OrderByTextRankOperator(string methodName, bool then): base(methodName)
    {
        _then = then;
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);

        var searchTerm = (string)(expression.Arguments[1].ReduceToConstant().Value ?? "");
        var function = (TextSearchFunction)expression.Arguments[2].ReduceToConstant().Value!;
        var regConfig = (string)expression.Arguments[3].ReduceToConstant().Value!;

        var mapping = usage.Options == null
            ? null
            : ((IReadOnlyStoreOptions)usage.Options).FindOrResolveDocumentType(usage.ElementType) as DocumentMapping;

        var ordering = new Ordering(expression, OrderingDirection.Desc)
        {
            TextRank = new TextRankOrdering(searchTerm, function, regConfig, mapping),

            // Ranking is a computed expression rather than a member, so it cannot be combined with a
            // Distinct(Select()) usage -- the same reason the ngram rank marks itself transformed.
            IsTransformed = true
        };

        // OrderBy replaces any ordering ahead of it; ThenBy appends after one.
        if (_then)
        {
            usage.OrderingExpressions.Add(ordering);
        }
        else
        {
            usage.OrderingExpressions.Insert(0, ordering);
        }
    }
}

internal class ThenByTextRankOperator: OrderByTextRankOperator
{
    public ThenByTextRankOperator(): base(nameof(QueryableExtensions.ThenByTextRank), true)
    {
    }
}
