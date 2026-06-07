using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// A Count()/LongCount() over a Distinct() projection of a SelectMany(child-collection) threw
// NotSupportedException "The database operator 'DISTINCT' cannot be used with non-simple types",
// even though the equivalent Distinct().ToList() worked: BuildSelectManyStatement re-applied
// DISTINCT to the count clause that ProcessSingleValueModeIfAny had already produced (and the
// Inner-merged IsDistinct flag never reached the count path, so a plain count of all rows was
// produced instead of count of distinct values).
public class Bug_distinct_count_over_selectmany: BugIntegrationContext
{
    public record Tag(string Value);
    public class Doc { public Guid Id { get; set; } public List<Tag> Tags { get; set; } = new(); }

    private async Task seedAsync()
    {
        StoreOptions(opts => opts.Schema.For<Doc>());
        await using var session = theStore.LightweightSession();
        // flattened values x,x,y,y,z -> 3 distinct, 5 total
        session.Store(
            new Doc { Id = Guid.NewGuid(), Tags = [new("x"), new("x"), new("y")] },
            new Doc { Id = Guid.NewGuid(), Tags = [new("y"), new("z")] });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task select_many_select_distinct_count()
    {
        await seedAsync();
        await using var query = theStore.QuerySession();
        var count = await query.Query<Doc>().SelectMany(d => d.Tags).Select(t => t.Value).Distinct().CountAsync();
        count.ShouldBe(3);
    }

    [Fact]
    public async Task select_many_select_distinct_long_count()
    {
        await seedAsync();
        await using var query = theStore.QuerySession();
        var count = await query.Query<Doc>().SelectMany(d => d.Tags).Select(t => t.Value).Distinct().LongCountAsync();
        count.ShouldBe(3L);
    }

    [Fact]
    public async Task select_many_select_count_without_distinct_is_unaffected()
    {
        await seedAsync();
        await using var query = theStore.QuerySession();
        var count = await query.Query<Doc>().SelectMany(d => d.Tags).Select(t => t.Value).CountAsync();
        count.ShouldBe(5);
    }

    [Fact]
    public async Task select_many_select_distinct_to_list_still_works()
    {
        await seedAsync();
        await using var query = theStore.QuerySession();
        var values = await query.Query<Doc>().SelectMany(d => d.Tags).Select(t => t.Value).Distinct().ToListAsync();
        values.OrderBy(v => v).ShouldBe(new[] { "x", "y", "z" });
    }
}
