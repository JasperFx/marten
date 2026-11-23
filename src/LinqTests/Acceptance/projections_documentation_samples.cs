using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;

namespace LinqTests.Acceptance.ProjectionSamples;

// Compile-only samples for docs/documents/querying/linq/projections.md. Nothing here runs as a test;
// the behavior each sample shows is covered by the tests named next to it.

public class Order
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderLine
{
    public string Product { get; set; } = "";
    public bool IsActive { get; set; }
    public int Quantity { get; set; }
}

public class UserName
{
    public string Name { get; set; } = "";
}

public class projections_documentation_samples
{
    // Covered by Bugs/Bug_5011_jsonb_build_object_select_projection.cs
    #region sample_server_side_json_projection
    public async Task server_side_json_projection(IQuerySession session)
    {
        // Only member access, so PostgreSQL builds the JSON with jsonb_build_object()
        var names = await session.Query<User>()
            .Select(x => new UserName { Name = x.FirstName })
            .ToListAsync();

        // A nested member is read through its JSON path, d.data -> 'Inner' ->> 'Number'
        var inner = await session.Query<Target>()
            .Select(x => new { x.Id, InnerNumber = x.Inner.Number })
            .ToListAsync();
    }
    #endregion

    // Covered by Bugs/Bug_5223_count_with_predicate_in_select.cs
    #region sample_count_a_child_collection_in_select
    public async Task count_a_child_collection_in_select(IQuerySession session)
    {
        var counts = await session.Query<Order>()
            .Select(x => new
            {
                x.Id,
                LineCount = x.Lines.Count(),
                ActiveLineCount = x.Lines.Count(line => line.IsActive),
                FooCount = x.Lines.Count(line => line.Product == "foo")
            })
            .ToListAsync();
    }
    #endregion

    // Covered by Bugs/Bug_5942_computed_top_level_select.cs
    #region sample_computed_values_in_select
    public async Task computed_values_in_select(IQuerySession session)
    {
        // Each of these is computed by PostgreSQL, not on the client
        var remainders = await session.Query<Target>().Select(x => x.Number % 3).ToListAsync();
        var labels = await session.Query<Target>().Select(x => x.String + "-" + x.Number).ToListAsync();
        var upper = await session.Query<Target>().Select(x => x.String.ToUpper()).ToListAsync();
        var ids = await session.Query<Target>().Select(x => x.Id.ToString()).ToListAsync();
        var widened = await session.Query<Target>().Select(x => (long)x.Number * 1_000_000_000L).ToListAsync();

        // Operators after the Select() work on the computed value
        var ones = await session.Query<Target>()
            .OrderBy(x => x.Number)
            .Select(x => x.Number % 3)
            .Where(r => r == 1)
            .ToListAsync();

        var distinct = await session.Query<Target>().Select(x => x.Number % 3).Distinct().ToListAsync();
        var ordered = await session.Query<Target>().Select(x => x.Number % 3).OrderBy(r => r).ToListAsync();
        var total = await session.Query<Target>().SumAsync(x => x.Number % 3);
    }
    #endregion

    // Covered by Bugs/Bug_3009_select_before_where.cs and Bugs/Bug_5955_order_by_after_projection.cs
    #region sample_operators_after_select
    public async Task operators_after_select(IQuerySession session)
    {
        // Where() after selecting a child object filters the documents on that child
        var inners = await session.Query<Target>()
            .Select(x => x.Inner)
            .Where(i => i != null && i.Number > 0)
            .ToListAsync();

        // OrderBy() after a scalar Select() orders the projected values
        var numbers = await session.Query<Target>()
            .Select(x => x.Number)
            .OrderByDescending(n => n)
            .ToListAsync();
    }
    #endregion

    // Covered by Bugs/Bug_5933_select_after_select_many.cs, Bugs/Bug_5941_string_members_after_select_many.cs
    // and Acceptance/first_and_single_after_select_many.cs
    #region sample_operators_after_select_many
    public async Task operators_after_select_many(IQuerySession session)
    {
        // Filter the flattened values, compute a value from each, then filter that
        var ones = await session.Query<Target>()
            .OrderBy(x => x.Number)
            .SelectMany(x => x.NumberArray)
            .Where(n => n > 25)
            .Select(n => n % 3)
            .Where(r => r == 1)
            .ToListAsync();

        // Order, de-duplicate and page the computed values
        var lengths = await session.Query<Target>()
            .SelectMany(x => x.StringArray)
            .Select(s => s.Length)
            .Distinct()
            .ToListAsync();

        var upper = await session.Query<Target>()
            .SelectMany(x => x.StringArray)
            .Select(s => s.ToUpper())
            .OrderBy(s => s)
            .ToListAsync();

        // A member of each child document
        var childNumbers = await session.Query<Target>()
            .OrderBy(x => x.Number)
            .SelectMany(x => x.Children)
            .Where(c => c.Flag)
            .Select(c => c.Number)
            .Take(10)
            .ToListAsync();

        // Element operators and aggregates over the flattened rows
        var firstChild = await session.Query<Target>().SelectMany(x => x.Children).FirstOrDefaultAsync();
        var longest = await session.Query<Target>().SelectMany(x => x.StringArray).Select(s => s.Length)
            .MaxAsync(l => l);
    }
    #endregion

    // Covered by Bugs/Bug_5928_paging_before_select_many.cs and Bugs/Bug_5920_count_over_paged_queries.cs
    #region sample_paging_before_and_after_select_many
    public async Task paging_before_and_after_select_many(IQuerySession session)
    {
        // Every child of the second page of five documents
        var childrenOfAPage = await session.Query<Target>()
            .OrderBy(x => x.Number)
            .Skip(5)
            .Take(5)
            .SelectMany(x => x.Children)
            .ToListAsync();

        // The second page of five children, across all the documents
        var pageOfChildren = await session.Query<Target>()
            .OrderBy(x => x.Number)
            .SelectMany(x => x.Children)
            .Skip(5)
            .Take(5)
            .ToListAsync();
    }
    #endregion
}
