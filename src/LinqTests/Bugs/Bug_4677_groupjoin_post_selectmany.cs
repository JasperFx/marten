using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Follow-up to #4677. Two LINQ-translation gaps were called out as known limitations
// in that PR; this file holds the regressions that pin the new behavior:
//
//   1. Aggregates over an *object* projection -- .SelectMany(... => new {...}).Sum(z => z.Member).
//      QueryableExtensions.SumAsync(z => z.Member) is implemented as .Select(z => z.Member).SumAsync(),
//      so the chain that arrives at re-linq looks like .Select(z => z.Member).Sum(). The Select
//      lands as a SelectExpression on the post-SelectMany usage; the FlattenedResultSelector's
//      object body needs to be reduced to just the aggregated source expression before the join
//      is rendered, so the scalar-aggregate path lights up.
//
//   2. .Select(...) / .Where(...) *after* the join's .SelectMany(...). CompileGroupJoin used to
//      ignore both: a post-SelectMany Select() returned the original anon-typed rows and a
//      post-SelectMany Where() was silently dropped. Both need to be expanded through the
//      FlattenedResultSelector's bindings -- z.Member -> the (x, c) source expression -- and
//      either folded into the join projection (Select) or routed to the appropriate CTE's
//      Where pipeline (Where).
//
// Shared seed: Parent { Score } with three children of varying Amount, plus an unmatched Parent
// for left-join coverage. Same shape as the upstream Bug_groupjoin_distinct_and_scalar_projection
// fixture so the cross-cutting math (sums, distinct counts) lines up.
public class Bug_4677_groupjoin_post_selectmany: BugIntegrationContext
{
    public class Parent
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
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
    private readonly Guid P3 = Guid.NewGuid(); // no children -- only present on left joins

    private async Task seedAsync()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Parent>();
            opts.Schema.For<Child>();
        });

        await using var session = theStore.LightweightSession();
        session.Store(
            new Parent { Id = P1, Name = "a", Score = 1.5m },
            new Parent { Id = P2, Name = "b", Score = 2.5m },
            new Parent { Id = P3, Name = "c", Score = 3.5m });
        session.Store(
            new Child { Id = Guid.NewGuid(), ParentId = P1, Amount = 10 },
            new Child { Id = Guid.NewGuid(), ParentId = P1, Amount = 10 },
            new Child { Id = Guid.NewGuid(), ParentId = P2, Amount = 20 });
        await session.SaveChangesAsync();
    }

    // ---- 1. Aggregates over an object projection -----------------------------------

    [Fact]
    public async Task object_projection_sum_of_inner_member()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        // 3 joined rows (P1->10, P1->10, P2->20). SumAsync(z => z.Amount) lowers to
        // .Select(z => z.Amount).SumAsync() via QueryableExtensions, which is the chain
        // the parser sees.
        var sum = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .SumAsync(z => z.Amount);
        sum.ShouldBe(40);
    }

    [Fact]
    public async Task object_projection_sum_of_outer_member()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        // x.p.Score over the 3 joined rows: 1.5 + 1.5 + 2.5 = 5.5.
        var sum = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Score })
            .SumAsync(z => z.Score);
        sum.ShouldBe(5.5m);
    }

    [Fact]
    public async Task object_projection_min_and_max()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var min = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .MinAsync(z => z.Amount);
        var max = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .MaxAsync(z => z.Amount);
        min.ShouldBe(10);
        max.ShouldBe(20);
    }

    [Fact]
    public async Task object_projection_average_returns_double()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var avg = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .AverageAsync(z => z.Amount);
        avg.ShouldBe(40d / 3d, 0.0001);
    }

    [Fact]
    public async Task object_projection_sum_over_left_join_ignores_unmatched()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        // Left join adds P3 with a null inner row; the rendered scalar c.Amount returns NULL on
        // that row at the SQL level and Postgres SUM ignores nulls -- so the result is still 40.
        // (We rely on SQL null semantics rather than a C# ternary because the projection-parser
        // only accepts direct member accesses; computed expressions are pinned as a separate
        // known limitation in the upstream PR's `scalar_projection_of_computed_expression`.)
        var sum = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children.DefaultIfEmpty(), (x, c) => new { c.Amount, x.p.Name })
            .SumAsync(z => z.Amount);
        sum.ShouldBe(40);
    }

    // ---- 2a. Select() after the join's SelectMany --------------------------------

    [Fact]
    public async Task post_select_many_select_reduces_to_scalar_member()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        // .Select(z => z.Amount) reduces the object projection to a single inner column,
        // matching the bare-scalar SelectMany result selector path.
        var amounts = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .Select(z => z.Amount)
            .ToListAsync();
        amounts.OrderBy(a => a).ShouldBe(new[] { 10, 10, 20 });
    }

    [Fact]
    public async Task post_select_many_select_reshuffles_to_new_anonymous_type()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        // The Select reshuffles the projection -- swaps positions and renames -- without any
        // computed expression. (Compute on the projected members like z.Amount * 2 falls under
        // the existing "computed scalar projection" limitation pinned in the upstream PR.)
        var rows = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .Select(z => new { ParentName = z.Name, ChildAmount = z.Amount })
            .ToListAsync();
        rows.Count.ShouldBe(3);
        rows.Select(r => r.ChildAmount).OrderBy(a => a).ShouldBe(new[] { 10, 10, 20 });
        rows.Select(r => r.ParentName).OrderBy(n => n).ShouldBe(new[] { "a", "a", "b" });
    }

    // ---- pinned limitations ------------------------------------------------------

    [Fact]
    public async Task post_select_many_where_touching_both_sides_throws_clear_error()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        // A filter referencing both x.p.* and c.* on the projected anon type can't be folded
        // onto either CTE -- pinned as a clear-error limitation rather than silently dropped
        // or mis-rendered.
        await Should.ThrowAsync<Marten.Exceptions.BadLinqExpressionException>(async () =>
            await q.Query<Parent>()
                .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
                .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name, x.p.Score })
                .Where(z => z.Amount > 5 && z.Name == "a")
                .ToListAsync());
    }

    // ---- 2b. Where() after the join's SelectMany ---------------------------------

    [Fact]
    public async Task post_select_many_where_filters_on_inner_member()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var rows = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .Where(z => z.Amount >= 15)
            .ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].Amount.ShouldBe(20);
        rows[0].Name.ShouldBe("b");
    }

    [Fact]
    public async Task post_select_many_where_filters_on_outer_member()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        var rows = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .Where(z => z.Name == "a")
            .ToListAsync();
        rows.Count.ShouldBe(2);
        rows.Select(r => r.Amount).OrderBy(a => a).ShouldBe(new[] { 10, 10 });
    }

    [Fact]
    public async Task post_select_many_where_then_sum_chains()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();

        // Where filters the joined rows to just P1's children, then Sum(z => z.Amount)
        // aggregates the object projection. Exercises both fixes in one chain.
        var sum = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new { c.Amount, x.p.Name })
            .Where(z => z.Name == "a")
            .SumAsync(z => z.Amount);
        sum.ShouldBe(20);
    }
}
