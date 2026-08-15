using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

/// <summary>
/// #5233: compiled-query parameter discovery walks <c>topStatement.AllFilters()</c>, which
/// enumerates WHERE clauses and never visits the select clause. Any parameter emitted into a
/// <c>Select()</c> projection is therefore bound once, at plan time, from whichever
/// <c>ICompiledQuery</c> instance built the plan, and every later execution of the cached plan
/// silently reuses that first value.
///
/// <para>
/// Three emitters can put a query member's value into a projection, and they do NOT all fail the
/// same way — which is the point of covering all three here rather than asserting the mechanism
/// once:
/// </para>
///
/// <list type="number">
/// <item><c>ConstantParameterSql</c> — a captured constant projected into
/// <c>jsonb_build_object(...)</c> (9.18, #5011).</item>
/// <item><c>ChildCollectionJsonPathCount</c> — <c>Count(predicate)</c> inside a projection
/// (#5223), whose values ride inside a jsonb <c>vars</c> payload and so need a *filter* to be
/// re-bound, not just a value match.</item>
/// <item>The client-side <c>LambdaSelectClause</c> fallback, whose compiled delegate closes over
/// the plan-time instance.</item>
/// </list>
/// </summary>
public class Bug_5233_compiled_query_select_clause_parameters: BugIntegrationContext
{
    private async Task seedAsync()
    {
        theSession.Store(new Shop5233
        {
            Id = Guid.NewGuid(),
            Name = "north",
            Lines =
            [
                new ShopLine5233 { Product = "foo" },
                new ShopLine5233 { Product = "foo" },
                new ShopLine5233 { Product = "bar" }
            ]
        });

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_constant_projected_into_the_select_list_rebinds()
    {
        await seedAsync();

        var first = await theSession.QueryAsync(new ShopWithLabel { Label = 111 });
        var second = await theSession.QueryAsync(new ShopWithLabel { Label = 222 });

        first.Single().Label.ShouldBe(111);
        second.Single().Label.ShouldBe(222,
            "the second execution of the cached plan must use its own Label, not the plan-time one");
    }

    [Fact]
    public async Task a_count_predicate_inside_the_select_list_rebinds()
    {
        await seedAsync();

        var foo = await theSession.QueryAsync(new ShopWithProductCount { Product = "foo" });
        var bar = await theSession.QueryAsync(new ShopWithProductCount { Product = "bar" });

        foo.Single().Count.ShouldBe(2);
        bar.Single().Count.ShouldBe(1,
            "the second execution of the cached plan must count its own Product, not the plan-time one");
    }

    [Fact]
    public async Task a_client_side_projection_referencing_a_query_member_is_refused()
    {
        await seedAsync();

        // Nothing can re-bind a delegate that was compiled once with the plan-time instance baked
        // into it, so this is refused rather than silently answering with the first value forever.
        var ex = await Should.ThrowAsync<InvalidCompiledQueryException>(async () =>
            await theSession.QueryAsync(new ShopWithComputedLabel { Suffix = "-one" }));

        ex.Message.ShouldContain("compiled once per plan");
    }

    [Fact]
    public async Task a_client_side_projection_that_captures_nothing_still_works()
    {
        await seedAsync();

        // The refusal above must be narrow. This projection also falls back to the client -- string
        // concatenation is not translatable -- but it reads only the source document, so the
        // compiled delegate is stable across invocations and the plan is perfectly cacheable.
        var first = await theSession.QueryAsync(new ShopWithStaticLabel());
        var second = await theSession.QueryAsync(new ShopWithStaticLabel());

        first.Single().Label.ShouldBe("north!");
        second.Single().Label.ShouldBe("north!");
    }
}

public class Shop5233
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public List<ShopLine5233> Lines { get; set; } = new();
}

public class ShopLine5233
{
    public string Product { get; set; }
}

public class Shop5233Dto
{
    public string Name { get; set; }
    public int Label { get; set; }
    public int Count { get; set; }
}

public class Shop5233LabelDto
{
    public string Label { get; set; }
}

/// <summary>ConstantParameterSql: a captured int projected straight into jsonb_build_object.</summary>
public class ShopWithLabel: ICompiledListQuery<Shop5233, Shop5233Dto>
{
    public int Label { get; set; }

    public Expression<Func<IMartenQueryable<Shop5233>, IEnumerable<Shop5233Dto>>> QueryIs()
    {
        return q => q.Select(x => new Shop5233Dto { Name = x.Name, Label = Label });
    }
}

/// <summary>ChildCollectionJsonPathCount: the predicate's value rides in a jsonb vars payload.</summary>
public class ShopWithProductCount: ICompiledListQuery<Shop5233, Shop5233Dto>
{
    public string Product { get; set; }

    public Expression<Func<IMartenQueryable<Shop5233>, IEnumerable<Shop5233Dto>>> QueryIs()
    {
        return q => q.Select(x => new Shop5233Dto
        {
            Name = x.Name, Count = x.Lines.Count(line => line.Product == Product)
        });
    }
}

/// <summary>LambdaSelectClause: string concatenation forces the client-side fallback.</summary>
public class ShopWithComputedLabel: ICompiledListQuery<Shop5233, Shop5233LabelDto>
{
    public string Suffix { get; set; }

    public Expression<Func<IMartenQueryable<Shop5233>, IEnumerable<Shop5233LabelDto>>> QueryIs()
    {
        return q => q.Select(x => new Shop5233LabelDto { Label = x.Name + Suffix });
    }
}

/// <summary>Falls back to the client, but captures nothing — must stay allowed.</summary>
public class ShopWithStaticLabel: ICompiledListQuery<Shop5233, Shop5233LabelDto>
{
    public Expression<Func<IMartenQueryable<Shop5233>, IEnumerable<Shop5233LabelDto>>> QueryIs()
    {
        return q => q.Select(x => new Shop5233LabelDto { Label = x.Name + "!" });
    }
}
