using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// GroupJoin + SelectMany had two distinct/projection gaps:
//   1. Distinct() was silently dropped over the join projection (JoinSelectClause was neither
//      IScalarSelectClause nor ICountClause, so neither distinct branch matched). Distinct().Count()
//      returned the full joined row count, and Distinct().ToList() returned non-distinct rows.
//   2. A bare scalar result selector ((x, t) => x.l.Id) built an empty NewObject and threw
//      "Sequence contains no elements" at materialization.
// Fix: JoinSelectClause implements IScalarSelectClause (renders DISTINCT(<projection>)); IsDistinct
// is transferred from the SelectMany usage in CompileGroupJoin; and a scalar result selector is
// rendered as to_jsonb(<scalar>) so the existing SerializationSelector + distinct machinery apply.
public class Bug_groupjoin_distinct_and_scalar_projection: BugIntegrationContext
{
    public enum Color { Red, Green, Blue }
    public class Parent
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Score { get; set; }
        public Color Tone { get; set; }
        public DateOnly Day { get; set; }
    }
    public class Child { public Guid Id { get; set; } public Guid ParentId { get; set; } public int Amount { get; set; } }
    public class AmountRow { public int Amount { get; set; } }

    private readonly Guid P1 = Guid.NewGuid();
    private readonly Guid P2 = Guid.NewGuid();
    private readonly Guid P3 = Guid.NewGuid(); // no children — only seen via left join

    private async Task seedAsync()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Parent>();
            opts.Schema.For<Child>();
        });

        await using var session = theStore.LightweightSession();
        session.Store(
            new Parent { Id = P1, Name = "a", Score = 1.5m, Tone = Color.Red, Day = new DateOnly(2026, 1, 1) },
            new Parent { Id = P2, Name = "b", Score = 2.5m, Tone = Color.Blue, Day = new DateOnly(2026, 2, 2) },
            new Parent { Id = P3, Name = "c", Score = 3.5m, Tone = Color.Green, Day = new DateOnly(2026, 3, 3) });
        // P1 has two children (amount 10, 10), P2 has one (amount 20). Joined rows for inner join = 3,
        // distinct parents = 2, distinct amounts = {10, 20} = 2.
        session.Store(
            new Child { Id = Guid.NewGuid(), ParentId = P1, Amount = 10 },
            new Child { Id = Guid.NewGuid(), ParentId = P1, Amount = 10 },
            new Child { Id = Guid.NewGuid(), ParentId = P2, Amount = 20 });
        await session.SaveChangesAsync();
    }

    private static IQueryable<AmountRow> InnerJoinObject(IQuerySession q) =>
        q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => new AmountRow { Amount = c.Amount });

    private static IQueryable<Guid> InnerJoinScalar(IQuerySession q) =>
        q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => x.p.Id);

    // ---- object projection ----

    [Fact]
    public async Task object_projection_count_without_distinct_is_unaffected()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        (await InnerJoinObject(q).CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task object_projection_distinct_tolist_dedupes()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var rows = await InnerJoinObject(q).Distinct().ToListAsync();
        rows.Select(r => r.Amount).OrderBy(a => a).ShouldBe(new[] { 10, 20 });
    }

    [Fact]
    public async Task object_projection_distinct_count()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        (await InnerJoinObject(q).Distinct().CountAsync()).ShouldBe(2);
    }

    // ---- scalar projection ----

    [Fact]
    public async Task scalar_projection_tolist_without_distinct()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var rows = await InnerJoinScalar(q).ToListAsync();
        rows.Count.ShouldBe(3);
        rows.ShouldAllBe(id => id == P1 || id == P2);
    }

    [Fact]
    public async Task scalar_projection_distinct_tolist_values()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var rows = await InnerJoinScalar(q).Distinct().ToListAsync();
        rows.OrderBy(x => x).ShouldBe(new[] { P1, P2 }.OrderBy(x => x));
    }

    [Fact]
    public async Task scalar_projection_distinct_count()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        (await InnerJoinScalar(q).Distinct().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task scalar_projection_distinct_long_count()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        (await InnerJoinScalar(q).Distinct().LongCountAsync()).ShouldBe(2L);
    }

    // ---- left join (DefaultIfEmpty) + scalar distinct: P3 has no children but still appears ----

    [Fact]
    public async Task left_join_scalar_distinct_count_includes_childless_outer()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var distinctParents = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children.DefaultIfEmpty(), (x, c) => x.p.Id)
            .Distinct()
            .CountAsync();

        distinctParents.ShouldBe(3);
    }

    [Fact]
    public async Task left_join_nullable_inner_scalar_yields_null_for_childless_outer()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        // P3 has no children, so the left join yields a null inner row; projecting an inner
        // member must materialize as null (the "data" column is null), not throw.
        var amounts = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children.DefaultIfEmpty(), (x, c) => (int?)c.Amount)
            .Distinct()
            .ToListAsync();

        // P1 children amount 10, 10; P2 amount 20; P3 -> null. Distinct = { 10, 20, null }.
        amounts.OrderBy(a => a.HasValue).ThenBy(a => a).ShouldBe(new int?[] { null, 10, 20 });
    }

    // ---- dedup correctness: DISTINCT must dedupe by the FULL projected value, not over-merge ----

    [Fact]
    public async Task object_projection_distinct_keeps_rows_differing_in_one_field()
    {
        StoreOptions(opts => { opts.Schema.For<Parent>(); opts.Schema.For<Child>(); });
        var p = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new Parent { Id = p, Name = "a" });
            // same parent (Name "a") but DIFFERENT amounts -> {a,10} and {a,20} must NOT merge
            session.Store(
                new Child { Id = Guid.NewGuid(), ParentId = p, Amount = 10 },
                new Child { Id = Guid.NewGuid(), ParentId = p, Amount = 20 });
            await session.SaveChangesAsync();
        }

        await using var q = theStore.QuerySession();
        var rows = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), x => x.Id, c => c.ParentId, (x, children) => new { x, children })
            .SelectMany(z => z.children, (z, c) => new { z.x.Name, c.Amount })
            .Distinct()
            .ToListAsync();

        rows.Count.ShouldBe(2);
        rows.Select(r => r.Amount).OrderBy(a => a).ShouldBe(new[] { 10, 20 });
    }

    // ---- scalar projection across types (round-trip via to_jsonb), distinct over an inner join ----

    [Fact]
    public async Task scalar_projection_string_distinct_values()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var names = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => x.p.Name)
            .Distinct().ToListAsync();
        names.OrderBy(n => n).ShouldBe(new[] { "a", "b" });
    }

    [Fact]
    public async Task scalar_projection_decimal_distinct_values()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var scores = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => x.p.Score)
            .Distinct().ToListAsync();
        scores.OrderBy(s => s).ShouldBe(new[] { 1.5m, 2.5m });
    }

    [Fact]
    public async Task scalar_projection_enum_distinct_values()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var tones = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => x.p.Tone)
            .Distinct().ToListAsync();
        tones.OrderBy(t => t).ShouldBe(new[] { Color.Red, Color.Blue }.OrderBy(t => t));
    }

    [Fact]
    public async Task scalar_projection_dateonly_distinct_values()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        var days = await q.Query<Parent>()
            .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
            .SelectMany(x => x.children, (x, c) => x.p.Day)
            .Distinct().ToListAsync();
        days.OrderBy(d => d).ShouldBe(new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 2) });
    }

    // ---- a non-member-access scalar body is not translatable: fail with a clear error ----

    [Fact]
    public async Task scalar_projection_of_computed_expression_throws_clear_error()
    {
        await seedAsync();
        await using var q = theStore.QuerySession();
        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
            await q.Query<Parent>()
                .GroupJoin(q.Query<Child>(), p => p.Id, c => c.ParentId, (p, children) => new { p, children })
                .SelectMany(x => x.children, (x, c) => c.Amount + 1)
                .ToListAsync());
    }
}
