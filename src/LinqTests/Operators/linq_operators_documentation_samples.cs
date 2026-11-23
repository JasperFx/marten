using System;
using System.Collections.Generic;
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

    // Covered by Acceptance/where_clauses.cs, Bugs/hashset_contains.cs, Bugs/Bug_5287_hashset_of_enums_contains.cs
    // and Bugs/Bug_inline_array_literal_as_contains_source.cs
    #region sample_contains_over_a_local_collection
    public async Task contains_over_a_local_collection(IQuerySession session, Guid[] ids)
    {
        // An array, List<T> or HashSet<T> of values from your code becomes a SQL IN test
        var byId = await session.Query<Target>().Where(x => ids.Contains(x.Id)).ToListAsync();

        var numbers = new List<int> { 1, 2, 3 };
        var byNumber = await session.Query<Target>().Where(x => numbers.Contains(x.Number)).ToListAsync();

        var colors = new HashSet<Colors> { Colors.Red, Colors.Blue };
        var byColor = await session.Query<Target>().Where(x => colors.Contains(x.Color)).ToListAsync();

        // An array written inline works too
        var byName = await session.Query<Target>()
            .Where(x => new[] { "Red", "Blue" }.Contains(x.String))
            .ToListAsync();

        // Negate it for NOT IN
        var others = await session.Query<Target>().Where(x => !ids.Contains(x.Id)).ToListAsync();
    }
    #endregion

    // Covered by compare_to_operator.cs and guid_compare_operator.cs
    #region sample_compare_to
    public async Task compare_to(IQuerySession session, Guid pivot, DateTime cutoff)
    {
        // CompareTo() compared with 0 becomes a plain comparison. It is the only way to write
        // a range comparison on a Guid, which has no < or > operator in C#
        var afterPivot = await session.Query<Target>().Where(x => x.OtherGuid.CompareTo(pivot) > 0).ToListAsync();

        var upToTen = await session.Query<Target>().Where(x => x.Number.CompareTo(10) <= 0).ToListAsync();
        var later = await session.Query<Target>().Where(x => x.Date.CompareTo(cutoff) > 0).ToListAsync();
    }
    #endregion

    // Covered by Bugs/Bug_5920_count_over_paged_queries.cs
    #region sample_count_after_take
    public async Task count_after_take(IQuerySession session)
    {
        // Counts the rows on the page: at most 10
        var onThePage = await session.Query<Target>().OrderBy(x => x.Number).Skip(20).Take(10).CountAsync();
    }
    #endregion

    // Covered by Acceptance/order_by_sql.cs
    #region sample_order_by_sql_then_by_sql
    public async Task order_by_sql_then_by_sql(IQuerySession session)
    {
        var results = await session.Query<Target>()
            .OrderBySql("d.data ->> 'String'")
            .ThenBySql("d.data ->> 'AnotherString' desc")
            .ToListAsync();
    }
    #endregion

    // Covered by group_by_operator.cs and grouped_projection_ordering.cs
    #region sample_group_by_ordering_and_paging
    public async Task group_by_ordering_and_paging(IQuerySession session)
    {
        // Order the groups by an aggregate, break ties by the key, then take the second group
        var secondBiggest = await session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => new { Color = g.Key, Total = g.Sum(x => x.Number) })
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Color)
            .Skip(1)
            .Take(1)
            .ToListAsync();

        // A scalar projection of the groups orders by the projected value
        var countsLargestFirst = await session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => g.Count())
            .OrderByDescending(x => x)
            .ToListAsync();
    }
    #endregion

    // Covered by group_by_operator.cs
    #region sample_group_by_counting_groups
    public async Task group_by_counting_groups(IQuerySession session)
    {
        // How many distinct colors there are
        var colorCount = await session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => g.Key)
            .CountAsync();
    }
    #endregion
}
