using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Linq.Caching;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests;

public class query_plan_cache_Tests: OneOffConfigurationsContext
{
    [Fact]
    public void same_shape_different_values_produces_same_key()
    {
        int number1 = 1;
        int number2 = 2;

        var (expr1, _) = whereExpression(number1);
        var (expr2, _) = whereExpression(number2);

        var shape1 = ExpressionShapeVisitor.Analyze(expr1);
        var shape2 = ExpressionShapeVisitor.Analyze(expr2);

        shape1.IsSupported.ShouldBeTrue();
        shape2.IsSupported.ShouldBeTrue();

        shape1.BuildKey(typeof(Target), typeof(Target))
            .ShouldBe(shape2.BuildKey(typeof(Target), typeof(Target)));
    }

    [Fact]
    public void different_shapes_produce_different_keys()
    {
        var (whereExpr, _) = whereExpression(1);
        var (skipTakeExpr, _) = skipTakeExpression(1, 2);

        var shape1 = ExpressionShapeVisitor.Analyze(whereExpr);
        var shape2 = ExpressionShapeVisitor.Analyze(skipTakeExpr);

        shape1.IsSupported.ShouldBeTrue();
        shape2.IsSupported.ShouldBeTrue();

        shape1.BuildKey(typeof(Target), typeof(Target))
            .ShouldNotBe(shape2.BuildKey(typeof(Target), typeof(Target)));
    }

    [Fact]
    public void different_filter_combinations_produce_different_keys()
    {
        var (whereOnlyExpr, _) = whereExpression(1);
        var (whereAndOrderExpr, _) = whereAndOrderExpression(1);

        var shape1 = ExpressionShapeVisitor.Analyze(whereOnlyExpr);
        var shape2 = ExpressionShapeVisitor.Analyze(whereAndOrderExpr);

        shape1.BuildKey(typeof(Target), typeof(Target))
            .ShouldNotBe(shape2.BuildKey(typeof(Target), typeof(Target)));
    }

    [Fact]
    public void unsupported_shapes_are_flagged()
    {
        IQueryable<Target> queryable = new Target[0].AsQueryable();
        var expr = queryable.Where(x => x.String.StartsWith("A")).Expression;

        var shape = ExpressionShapeVisitor.Analyze(expr);

        shape.IsSupported.ShouldBeFalse();
    }

    [Fact]
    public async Task cached_plan_produces_same_results_as_uncached()
    {
        StoreOptions(_ => _.Linq.EnableQueryPlanCaching());

        using var session = theStore.LightweightSession();
        session.Store(new Target { Number = 1, String = "one" });
        session.Store(new Target { Number = 2, String = "two" });
        session.Store(new Target { Number = 3, String = "three" });
        await session.SaveChangesAsync();

        var uncached = await session.Query<Target>().Where(x => x.Number == 2).ToListAsync();

        var cached = await session.Query<Target>().Where(x => x.Number == 2)
            .ToListAsync(default, QueryPlanCaching.Cached);

        cached.Count.ShouldBe(uncached.Count);
        cached.Single().Number.ShouldBe(2);

        // Different filter value, same shape -- should hit the cache on the second call.
        var cached2 = await session.Query<Target>().Where(x => x.Number == 3)
            .ToListAsync(default, QueryPlanCaching.Cached);

        cached2.Single().Number.ShouldBe(3);

        theStore.Options.Linq.QueryPlanCache.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task cache_respects_max_entries()
    {
        StoreOptions(_ => _.Linq.QueryPlanCache = QueryPlanCache.PerShape(maxEntries: 1));

        using var session = theStore.LightweightSession();
        session.Store(new Target { Number = 1 });
        await session.SaveChangesAsync();

        await session.Query<Target>().Where(x => x.Number == 1)
            .ToListAsync(default, QueryPlanCaching.Cached);
        await session.Query<Target>().Where(x => x.String == "abc")
            .ToListAsync(default, QueryPlanCaching.Cached);

        theStore.Options.Linq.QueryPlanCache.Count.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task opt_in_configuration_is_required()
    {
        // No StoreOptions() call enabling the cache -- QueryPlanCache.Disabled is the default.
        using var session = theStore.LightweightSession();
        session.Store(new Target { Number = 1 });
        await session.SaveChangesAsync();

        theStore.Options.Linq.QueryPlanCache.Enabled.ShouldBeFalse();

        var results = await session.Query<Target>().Where(x => x.Number == 1)
            .ToListAsync(default, QueryPlanCaching.Cached);

        results.Single().Number.ShouldBe(1);
        theStore.Options.Linq.QueryPlanCache.Count.ShouldBe(0);
    }

    private static (System.Linq.Expressions.Expression, int) whereExpression(int number)
    {
        IQueryable<Target> queryable = new Target[0].AsQueryable();
        var expr = queryable.Where(x => x.Number == number).Expression;
        return (expr, number);
    }

    private static (System.Linq.Expressions.Expression, int) whereAndOrderExpression(int number)
    {
        IQueryable<Target> queryable = new Target[0].AsQueryable();
        var expr = queryable.Where(x => x.Number == number).OrderBy(x => x.Number).Expression;
        return (expr, number);
    }

    private static (System.Linq.Expressions.Expression, int) skipTakeExpression(int skip, int take)
    {
        IQueryable<Target> queryable = new Target[0].AsQueryable();
        var expr = queryable.Skip(skip).Take(take).Expression;
        return (expr, skip);
    }
}
