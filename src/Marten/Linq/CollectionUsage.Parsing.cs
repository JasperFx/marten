#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Parsing.Operators;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq;

public partial class CollectionUsage
{
    public List<Ordering> OrderingExpressions { get; } = new();

    // #5959: where the next ordering key goes. See AddOrdering().
    private int _orderingGroupStart;

    /// <summary>
    ///     #5959: add one ordering key. Operators are applied outermost first, so the keys of a chain arrive last
    ///     to first. A <c>ThenBy()</c> key goes in front of the keys of its own chain; an <c>OrderBy()</c> key
    ///     starts the chain, and once it is in, every key that arrives after it belongs to an EARLIER
    ///     <c>OrderBy()</c>. LINQ's sort is stable, so an earlier ordering only breaks the ties a later one
    ///     leaves: its keys go after this chain's, not in front of them.
    /// </summary>
    /// <param name="ordering">The key, or null for a key that is dropped (#5950) but still starts its chain</param>
    /// <param name="startsTheOrdering">True for OrderBy() and its kin, false for ThenBy() and its kin</param>
    internal void AddOrdering(Ordering? ordering, bool startsTheOrdering)
    {
        if (ordering != null)
        {
            OrderingExpressions.Insert(_orderingGroupStart, ordering);
        }

        if (startsTheOrdering)
        {
            _orderingGroupStart = OrderingExpressions.Count;
        }
    }
    public List<Expression> WhereExpressions { get; } = new();
    public Expression? SelectExpression { get; set; }

    /// <summary>
    /// The key selector of a <c>DistinctBy(keySelector)</c> operator, if any.
    /// Translated to PostgreSQL <c>SELECT DISTINCT ON (key)</c>. See #4565.
    /// </summary>
    public Expression? DistinctByExpression { get; set; }

    public void AddWhereClause(MethodCallExpression expression)
    {
        if (expression.Arguments.Count == 1)
        {
            return;
        }

        var where = expression.Arguments[1];
        if (where is UnaryExpression e)
        {
            where = e.Operand;
        }

        if (where is LambdaExpression l)
        {
            where = l.Body;
        }

        WhereExpressions.Add(where);
    }

    public void AddSelectClause(MethodCallExpression expression)
    {
        var select = expression.Arguments[1];
        if (select is UnaryExpression e)
        {
            select = e.Operand;
        }

        if (select is LambdaExpression l)
        {
            select = l.Body;
        }

        SelectExpression = select;
    }
}
