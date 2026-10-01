using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.ChildCollections;

/// <summary>
///     https://github.com/JasperFx/marten/issues/5549.
/// </summary>
/// <remarks>
///     A sub-query is parsed against a member tree rooted at the element type, so inside
///     <c>Twos.Any(t =&gt; t.Threes.Any(...))</c> the <c>Threes</c> member reports
///     <c>d.data -&gt; 'Threes'</c> -- <c>d.data</c> standing for one <c>Deep2</c>. Lifting that
///     containment filter out to the enclosing <c>Any()</c> has to move the locator along with the
///     payload. It did not, so the query went out as
///     <c>d.data -&gt; 'Threes' @&gt; '[{"Twos":[...]}]'</c>: valid SQL against a column that does not
///     exist on the document, matching nothing and raising no error.
/// </remarks>
public class Bug_5549_nested_any_containment_locator: OneOffConfigurationsContext
{
    private readonly Deep1 _redBillA = new()
    {
        Name = "red-bill-A",
        Twos =
        [
            new Deep2
            {
                Name = "A",
                Tags = ["red", "green"],
                Threes = [new Deep3 { Name = "Bill", Number = 3, Tags = ["x"] }]
            }
        ]
    };

    private readonly Deep1 _blueBillB = new()
    {
        Name = "blue-bill-B",
        Twos =
        [
            new Deep2
            {
                Name = "B",
                Tags = ["blue"],
                Threes = [new Deep3 { Name = "Bill", Number = 9, Tags = ["y"] }]
            }
        ]
    };

    private readonly Deep1 _redJackA = new()
    {
        Name = "red-jack-A",
        Twos =
        [
            new Deep2
            {
                Name = "A",
                Tags = ["red", "blue"],
                Threes = [new Deep3 { Name = "Jack", Number = 3, Tags = ["x", "y"] }]
            }
        ]
    };

    private readonly Deep1 _empty = new()
    {
        Name = "empty", Twos = [new Deep2 { Name = "C" }]
    };

    // The two halves of every predicate below are satisfied, but never by the same Deep2. A
    // containment payload that collapses sibling Any() calls into one element would be right
    // about the others and wrong about this one.
    private readonly Deep1 _splitAcrossTwos = new()
    {
        Name = "split",
        Twos =
        [
            new Deep2 { Name = "D", Tags = ["red"] },
            new Deep2 { Name = "E", Threes = [new Deep3 { Name = "Bill", Number = 1 }] }
        ]
    };

    // One Deep2, but the two halves of a sibling-Any predicate land on different Deep3 elements.
    // `@>` is satisfied by that; a payload that folds both predicates into one element is not.
    private readonly Deep1 _splitAcrossThrees = new()
    {
        Name = "split-threes",
        Twos =
        [
            new Deep2
            {
                Name = "F",
                Threes = [new Deep3 { Name = "Bill", Number = 99 }, new Deep3 { Name = "Zed", Number = 3 }]
            }
        ]
    };

    private async Task seed()
    {
        await theStore.BulkInsertAsync(new[]
        {
            _redBillA, _blueBillB, _redJackA, _empty, _splitAcrossTwos, _splitAcrossThrees
        });
    }

    private async Task<string[]> namesFor(Func<IQueryable<Deep1>, IQueryable<Deep1>> apply)
    {
        await seed();
        var results = await apply(theSession.Query<Deep1>()).ToListAsync();
        return results.Select(x => x.Name).OrderBy(x => x).ToArray();
    }

    [Fact]
    public async Task nested_any_targets_the_outer_collection()
    {
        var sql = theSession.Query<Deep1>()
            .Where(x => x.Twos.Any(t => t.Threes.Any(h => h.Name == "Bill")))
            .ToCommand().CommandText;

        sql.ShouldContain("d.data -> 'Twos' @>");
        sql.ShouldNotContain("'Threes' @>");
    }

    [Fact]
    public async Task nested_any_with_no_sibling_predicate()
    {
        var names = await namesFor(q => q.Where(x => x.Twos.Any(t => t.Threes.Any(h => h.Name == "Bill"))));

        names.ShouldBe(["blue-bill-B", "red-bill-A", "split", "split-threes"]);
    }

    [Fact]
    public async Task nested_any_with_a_sibling_predicate()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Name == "A" && t.Threes.Any(h => h.Name == "Bill"))));

        names.ShouldBe(["red-bill-A"]);
    }

    [Fact]
    public async Task nested_any_with_two_predicates_in_the_inner_lambda()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Threes.Any(h => h.Name == "Bill" && h.Number == 3))));

        names.ShouldBe(["red-bill-A"]);
    }

    [Fact]
    public async Task negated_nested_any()
    {
        var names = await namesFor(q => q.Where(x => !x.Twos.Any(t => t.Threes.Any(h => h.Name == "Bill"))));

        names.ShouldBe(["empty", "red-jack-A"]);
    }

    [Fact]
    public async Task nested_any_as_one_branch_of_an_or()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Threes.Any(h => h.Name == "Jack") || t.Name == "B")));

        names.ShouldBe(["blue-bill-B", "red-jack-A"]);
    }

    [Fact]
    public async Task nested_any_combined_with_a_root_predicate()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Threes.Any(h => h.Name == "Bill")) || x.Name == "empty"));

        names.ShouldBe(["blue-bill-B", "empty", "red-bill-A", "split", "split-threes"]);
    }

    [Fact]
    public async Task three_levels_deep()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Threes.Any(h => h.Tags.Any(g => g == "y")))));

        names.ShouldBe(["blue-bill-B", "red-jack-A"]);
    }

    [Fact]
    public async Task three_levels_deep_with_a_sibling_predicate()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Name == "A" && t.Threes.Any(h => h.Tags.Any(g => g == "y")))));

        names.ShouldBe(["red-jack-A"]);
    }

    [Fact]
    public async Task nested_value_collection_any()
    {
        var names = await namesFor(q => q.Where(x => x.Twos.Any(t => t.Tags.Any(g => g == "red"))));

        names.ShouldBe(["red-bill-A", "red-jack-A", "split"]);
    }

    [Fact]
    public async Task nested_value_collection_contains_with_a_sibling_predicate()
    {
        var names = await namesFor(q => q.Where(x => x.Twos.Any(t => t.Tags.Contains("red") && t.Name == "A")));

        names.ShouldBe(["red-bill-A", "red-jack-A"]);
    }

    [Fact]
    public async Task two_sibling_any_calls_over_the_same_child_collection()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t =>
                t.Threes.Any(h => h.Name == "Bill") && t.Threes.Any(h => h.Number == 3))));

        // "split-threes" satisfies the two calls with two different Deep3 elements, which is what
        // two sibling Any() calls ask for -- contrast with the single-lambda test above
        names.ShouldBe(["red-bill-A", "split-threes"]);
    }

    [Fact]
    public async Task two_sibling_any_calls_over_the_same_value_collection()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Tags.Any(g => g == "red") && t.Tags.Any(g => g == "blue"))));

        names.ShouldBe(["red-jack-A"]);
    }

    [Fact]
    public async Task sibling_any_calls_over_different_collections_must_hit_the_same_element()
    {
        var names = await namesFor(q =>
            q.Where(x => x.Twos.Any(t => t.Tags.Any(g => g == "red") && t.Threes.Any(h => h.Name == "Bill"))));

        // "split" has a Deep2 tagged red and a Deep2 holding a Bill, but no single Deep2 with both
        names.ShouldBe(["red-bill-A"]);
    }

    [Fact]
    public async Task nested_any_matching_nothing()
    {
        var names = await namesFor(q => q.Where(x => x.Twos.Any(t => t.Threes.Any(h => h.Name == "Nobody"))));

        names.ShouldBeEmpty();
    }
}

public class Deep1
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public IList<Deep2> Twos { get; set; } = new List<Deep2>();
}

public class Deep2
{
    public string Name { get; set; }
    public IList<string> Tags { get; set; } = new List<string>();
    public IList<Deep3> Threes { get; set; } = new List<Deep3>();
}

public class Deep3
{
    public string Name { get; set; }
    public int Number { get; set; }
    public IList<string> Tags { get; set; } = new List<string>();
}
