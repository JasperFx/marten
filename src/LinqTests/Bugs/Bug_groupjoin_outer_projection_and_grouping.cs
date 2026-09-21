using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Two gaps left after #4677 / the post-SelectMany follow-up, both hit by the same real query: a
// werkvoorraad screen that filters its rows on a value that lives on the *joined* document, and then
// serves that same filtered set twice -- once as an ordered table, once as counts grouped by category.
//
//   1. .SelectMany(..., (x, _) => x.Outer) -- projecting the OUTER document out of the join -- cannot
//      be ordered. AnonProjectionExpander only expands an anonymous projection or the inner identity
//      ((x, c) => c), so CompileGroupJoin throws BadLinqExpressionException for this shape even though
//      the ordering is a plain member of the outer CTE.
//
//   2. .GroupBy(...).Select(g => new {...}) after the join is not compiled at all: BuildTopStatement
//      dispatches GroupBy before the join is reached, so the join never renders a GROUP BY and the
//      query falls through to "Marten does not know how to use result type ...".
public class Bug_groupjoin_outer_projection_and_grouping: BugIntegrationContext
{
    public class Parent
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal Score { get; set; }
    }

    public class Child
    {
        public Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public int Amount { get; set; }
    }

    private readonly Guid P1 = Guid.NewGuid();
    private readonly Guid P2 = Guid.NewGuid();
    private readonly Guid P3 = Guid.NewGuid(); // no children

    private async Task seedAsync()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Parent>();
            opts.Schema.For<Child>();
        });

        await using var session = theStore.LightweightSession();
        session.Store(
            new Parent { Id = P1, Name = "a", Category = "x", Score = 1.5m },
            new Parent { Id = P2, Name = "b", Category = "x", Score = 2.5m },
            new Parent { Id = P3, Name = "c", Category = "y", Score = 3.5m });
        session.Store(
            new Child { Id = Guid.NewGuid(), ParentId = P1, Amount = 10 },
            new Child { Id = Guid.NewGuid(), ParentId = P1, Amount = 10 },
            new Child { Id = Guid.NewGuid(), ParentId = P2, Amount = 20 });
        await session.SaveChangesAsync();
    }

    // ---- 1. Ordering an outer projection --------------------------------------------

    [Fact]
    public async Task outer_projection_returns_the_joined_outer_rows()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var parents = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, _) => x.p)
            .ToListAsync();

        // One row per matched child: P1 twice, P2 once, P3 not at all.
        parents.Select(x => x.Name).OrderBy(x => x).ShouldBe(["a", "a", "b"]);
    }

    [Fact]
    public async Task outer_projection_can_be_ordered_after_the_selectmany()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var parents = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, _) => x.p)
            .OrderByDescending(p => p.Score)
            .ToListAsync();

        parents.Select(x => x.Name).ShouldBe(["b", "a", "a"]);
    }

    [Fact]
    public async Task outer_projection_can_be_filtered_after_the_selectmany()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var parents = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, _) => x.p)
            .Where(p => p.Score > 2m)
            .ToListAsync();

        parents.ShouldHaveSingleItem().Name.ShouldBe("b");
    }

    // ---- 2. Grouping over the join ---------------------------------------------------

    [Fact]
    public async Task group_by_over_the_join_counts_and_sums_the_joined_rows()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var grouped = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { x.p.Name, x.p.Category, c.Amount })
            .GroupBy(z => z.Name)
            .Select(g => new { Name = g.Key, Rows = g.Count(), Total = g.Sum(z => z.Amount) })
            .ToListAsync();

        grouped.Count.ShouldBe(2);
        var a = grouped.Single(x => x.Name == "a");
        a.Rows.ShouldBe(2);
        a.Total.ShouldBe(20);
        var b = grouped.Single(x => x.Name == "b");
        b.Rows.ShouldBe(1);
        b.Total.ShouldBe(20);
    }

    [Fact]
    public async Task group_by_over_the_join_respects_a_filter_on_the_inner_side()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var grouped = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>().Where(c => c.Amount > 15), p => p.Id, c => c.ParentId,
                (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { x.p.Category, c.Amount })
            .GroupBy(z => z.Category)
            .Select(g => new { Category = g.Key, Rows = g.Count() })
            .ToListAsync();

        grouped.ShouldHaveSingleItem();
        grouped[0].Category.ShouldBe("x");
        grouped[0].Rows.ShouldBe(1);
    }
}
