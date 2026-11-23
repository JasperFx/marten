using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;

namespace LinqTests.Operators;

// Compile-only samples for docs/documents/querying/linq/operators.md. Nothing here runs as a test;
// the behavior each sample shows is covered by the tests named next to it.
public class linq_operators_documentation_samples
{
    // Covered by Bugs/query_with_order_by.cs and Bugs/Bug_2649_select_and_distinct_string_with_ordering.cs
    #region sample_order_by_case_insensitive
    public async Task order_by_case_insensitive(IQuerySession session)
    {
        // Invariant culture, ignoring case
        var byInvariant = await session.Query<Target>()
            .OrderBy(x => x.String, StringComparer.InvariantCultureIgnoreCase)
            .ToListAsync();

        // Ordinal, ignoring case
        var byOrdinal = await session.Query<Target>()
            .OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase)
            .ToListAsync();
    }
    #endregion

    // Covered by group_by_operator.cs
    #region sample_group_by_composite_key
    public async Task group_by_composite_key(IQuerySession session)
    {
        var results = await session.Query<Target>()
            .GroupBy(x => new { x.Color, x.String })
            .Select(g => new { Color = g.Key.Color, Text = g.Key.String, Count = g.Count() })
            .ToListAsync();
    }
    #endregion

    #region sample_group_by_where_before
    public async Task group_by_where_before(IQuerySession session)
    {
        var results = await session.Query<Target>()
            .Where(x => x.Number > 20)
            .GroupBy(x => x.Color)
            .Select(g => new { Color = g.Key, Count = g.Count() })
            .ToListAsync();
    }
    #endregion

    #region sample_group_by_having
    public async Task group_by_having(IQuerySession session)
    {
        // The Where() after GroupBy() becomes a SQL HAVING clause
        var results = await session.Query<Target>()
            .GroupBy(x => x.Color)
            .Where(g => g.Count() > 1)
            .Select(g => new { Color = g.Key, Count = g.Count() })
            .ToListAsync();
    }
    #endregion

    #region sample_count_by_after_loading
    public async Task count_by_after_loading(IQuerySession session)
    {
        // CountBy(), AggregateBy() and Index() only exist on Enumerable,
        // so load the rows first, then apply them to the list in memory
        var targets = await session.Query<Target>()
            .Where(x => x.Number > 5)
            .ToListAsync();

        var countsByColor = targets.CountBy(x => x.Color);
        var numbered = targets.Index();
    }
    #endregion
}
