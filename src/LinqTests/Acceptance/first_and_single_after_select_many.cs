using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

/// <summary>
/// #5921: First / FirstOrDefault / Single / SingleOrDefault after SelectMany(), checked against LINQ to Objects
/// over the same data, empty-sequence and more-than-one behaviour included.
/// </summary>
public class first_and_single_after_select_many: IntegrationContext
{
    // Several tests assert on the whole flattened sequence, so they must only see the holders seeded here
    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(SelectManyHolder), typeof(Target)];

    public first_and_single_after_select_many(DefaultStoreFixture fixture): base(fixture)
    {
    }

    // The holders with nothing to flatten come first, so a LIMIT applied to the parent rows rather than to
    // the flattened ones would find nothing
    private static readonly SelectManyHolder[] Holders =
    [
        new() { Id = Guid.NewGuid(), Name = "empty", Items = [], Numbers = [] },
        new()
        {
            Id = Guid.NewGuid(), Name = "one",
            Items = [new() { Name = "a", Number = 1 }, new() { Name = "b", Number = 5 }],
            Numbers = [3, 7]
        },
        new() { Id = Guid.NewGuid(), Name = "also-empty", Items = [], Numbers = [] },
        new()
        {
            Id = Guid.NewGuid(), Name = "three",
            Items = [new() { Name = "c", Number = 9 }, new() { Name = "d", Number = 2 }, new() { Name = "e", Number = 5 }],
            Numbers = [10, 1, 4]
        }
    ];

    private async Task seed()
    {
        // One per call so the physical order follows the array
        foreach (var holder in Holders)
        {
            theSession.Store(holder);
            await theSession.SaveChangesAsync();
        }
    }

    #region child documents

    [Fact]
    public async Task first_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).FirstAsync();

        // Unordered, so any flattened row is a correct answer
        Holders.SelectMany(x => x.Items).ShouldContain(item);
    }

    [Fact]
    public async Task first_or_default_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).FirstOrDefaultAsync();

        item.ShouldNotBeNull();
        Holders.SelectMany(x => x.Items).ShouldContain(item);
    }

    [Fact]
    public async Task first_with_predicate_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).FirstAsync(x => x.Number == 9);

        item.ShouldBe(Holders.SelectMany(x => x.Items).First(x => x.Number == 9));
    }

    [Fact]
    public async Task first_with_predicate_over_child_documents_miss()
    {
        await seed();

        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).FirstAsync(x => x.Number == 100),
            () => Holders.SelectMany(x => x.Items).First(x => x.Number == 100));
    }

    [Fact]
    public async Task first_or_default_with_predicate_over_child_documents_miss()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items)
            .FirstOrDefaultAsync(x => x.Number == 100);

        item.ShouldBe(Holders.SelectMany(x => x.Items).FirstOrDefault(x => x.Number == 100));
        item.ShouldBeNull();
    }

    [Fact]
    public async Task first_over_child_documents_when_nothing_flattens()
    {
        await seed();

        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().Where(x => x.Name == "empty").SelectMany(x => x.Items).FirstAsync(),
            () => Holders.Where(x => x.Name == "empty").SelectMany(x => x.Items).First());
    }

    [Fact]
    public async Task first_or_default_over_child_documents_when_nothing_flattens()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().Where(x => x.Name == "empty").SelectMany(x => x.Items)
            .FirstOrDefaultAsync();

        item.ShouldBeNull();
    }

    [Fact]
    public async Task single_with_predicate_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).SingleAsync(x => x.Name == "d");

        item.ShouldBe(Holders.SelectMany(x => x.Items).Single(x => x.Name == "d"));
    }

    [Fact]
    public async Task single_over_child_documents_with_many_rows()
    {
        await seed();

        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).SingleAsync(),
            () => Holders.SelectMany(x => x.Items).Single());
    }

    [Fact]
    public async Task single_with_predicate_over_child_documents_with_two_matches()
    {
        await seed();

        // Number == 5 is in two different holders, so this needs LIMIT 2 on the flattened rows
        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).SingleAsync(x => x.Number == 5),
            () => Holders.SelectMany(x => x.Items).Single(x => x.Number == 5));
    }

    [Fact]
    public async Task single_with_predicate_over_child_documents_miss()
    {
        await seed();

        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).SingleAsync(x => x.Number == 100),
            () => Holders.SelectMany(x => x.Items).Single(x => x.Number == 100));
    }

    [Fact]
    public async Task single_or_default_with_predicate_over_child_documents()
    {
        await seed();

        var hit = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items)
            .SingleOrDefaultAsync(x => x.Name == "c");
        hit.ShouldBe(Holders.SelectMany(x => x.Items).SingleOrDefault(x => x.Name == "c"));

        var miss = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items)
            .SingleOrDefaultAsync(x => x.Name == "zzz");
        miss.ShouldBeNull();
    }

    [Fact]
    public async Task single_or_default_with_predicate_over_child_documents_with_two_matches()
    {
        await seed();

        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).SingleOrDefaultAsync(x => x.Number == 5),
            () => Holders.SelectMany(x => x.Items).SingleOrDefault(x => x.Number == 5));
    }

    [Fact]
    public async Task where_then_first_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).Where(x => x.Number > 8)
            .FirstAsync();

        item.ShouldBe(Holders.SelectMany(x => x.Items).Where(x => x.Number > 8).First());
    }

    [Fact]
    public async Task where_then_single_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).Where(x => x.Name == "a")
            .SingleAsync();

        item.ShouldBe(Holders.SelectMany(x => x.Items).Where(x => x.Name == "a").Single());
    }

    [Fact]
    public async Task order_by_then_first_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items).OrderBy(x => x.Number)
            .FirstAsync();

        item.ShouldBe(Holders.SelectMany(x => x.Items).OrderBy(x => x.Number).First());
    }

    [Fact]
    public async Task order_by_descending_then_first_or_default_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items)
            .OrderByDescending(x => x.Number).FirstOrDefaultAsync();

        item.ShouldBe(Holders.SelectMany(x => x.Items).OrderByDescending(x => x.Number).FirstOrDefault());
    }

    [Fact]
    public async Task where_and_order_by_then_first_with_predicate_over_child_documents()
    {
        await seed();

        var item = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Items)
            .Where(x => x.Number < 9).OrderByDescending(x => x.Number).FirstAsync(x => x.Name != "e");

        item.ShouldBe(Holders.SelectMany(x => x.Items)
            .Where(x => x.Number < 9).OrderByDescending(x => x.Number).First(x => x.Name != "e"));
    }

    #endregion

    #region value collections

    [Fact]
    public async Task first_over_a_value_collection()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).FirstAsync();

        Holders.SelectMany(x => x.Numbers).ShouldContain(number);
    }

    [Fact]
    public async Task first_or_default_over_a_value_collection()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).FirstOrDefaultAsync();

        Holders.SelectMany(x => x.Numbers).ShouldContain(number);
    }

    [Fact]
    public async Task first_with_predicate_over_a_value_collection()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).FirstAsync(x => x > 8);

        number.ShouldBe(Holders.SelectMany(x => x.Numbers).First(x => x > 8));
    }

    [Fact]
    public async Task first_with_predicate_over_a_value_collection_miss()
    {
        await seed();

        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).FirstAsync(x => x > 100),
            () => Holders.SelectMany(x => x.Numbers).First(x => x > 100));
    }

    [Fact]
    public async Task first_or_default_with_predicate_over_a_value_collection_miss()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers)
            .FirstOrDefaultAsync(x => x > 100);

        number.ShouldBe(Holders.SelectMany(x => x.Numbers).FirstOrDefault(x => x > 100));
        number.ShouldBe(0);
    }

    [Fact]
    public async Task first_or_default_over_a_value_collection_when_nothing_flattens()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().Where(x => x.Name == "empty")
            .SelectMany(x => x.Numbers).FirstOrDefaultAsync();

        number.ShouldBe(0);
    }

    [Fact]
    public async Task single_with_predicate_over_a_value_collection()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).SingleAsync(x => x > 8);

        number.ShouldBe(Holders.SelectMany(x => x.Numbers).Single(x => x > 8));
    }

    [Fact]
    public async Task single_over_a_value_collection_with_many_rows()
    {
        await seed();

        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).SingleAsync(),
            () => Holders.SelectMany(x => x.Numbers).Single());
    }

    [Fact]
    public async Task single_or_default_with_predicate_over_a_value_collection()
    {
        await seed();

        var hit = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers)
            .SingleOrDefaultAsync(x => x == 7);
        hit.ShouldBe(Holders.SelectMany(x => x.Numbers).SingleOrDefault(x => x == 7));

        var miss = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers)
            .SingleOrDefaultAsync(x => x == 100);
        miss.ShouldBe(0);

        // 3 and 4 match in different holders
        await shouldThrowLikeLinqToObjects(
            () => theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).SingleOrDefaultAsync(x => x == 3 || x == 4),
            () => Holders.SelectMany(x => x.Numbers).SingleOrDefault(x => x == 3 || x == 4));
    }

    [Fact]
    public async Task where_and_order_by_then_first_over_a_value_collection()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers)
            .Where(x => x > 2).OrderBy(x => x).FirstAsync();

        number.ShouldBe(Holders.SelectMany(x => x.Numbers).Where(x => x > 2).OrderBy(x => x).First());
    }

    [Fact]
    public async Task order_by_descending_then_first_or_default_over_a_value_collection()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers)
            .OrderByDescending(x => x).FirstOrDefaultAsync();

        number.ShouldBe(Holders.SelectMany(x => x.Numbers).OrderByDescending(x => x).FirstOrDefault());
    }

    [Fact]
    public async Task where_then_single_over_a_value_collection()
    {
        await seed();

        var number = await theSession.Query<SelectManyHolder>().SelectMany(x => x.Numbers).Where(x => x == 10)
            .SingleAsync();

        number.ShouldBe(10);
    }

    #endregion

    #region where the LIMIT goes

    [Fact]
    public async Task first_limits_the_flattened_rows_to_one_and_not_the_parents()
    {
        await limitShouldBeOnlyOnTheFlattenedRows(q => q.SelectMany(x => x.Items).FirstAsync(), 1);
    }

    [Fact]
    public async Task first_or_default_limits_the_flattened_value_rows_to_one()
    {
        await limitShouldBeOnlyOnTheFlattenedRows(q => q.SelectMany(x => x.Numbers).FirstOrDefaultAsync(), 1);
    }

    [Fact]
    public async Task single_limits_the_flattened_rows_to_two()
    {
        await limitShouldBeOnlyOnTheFlattenedRows(q => q.SelectMany(x => x.Items).SingleOrDefaultAsync(x => x.Name == "a"), 2);
    }

    [Fact]
    public async Task single_limits_the_flattened_value_rows_to_two()
    {
        await limitShouldBeOnlyOnTheFlattenedRows(q => q.SelectMany(x => x.Numbers).SingleOrDefaultAsync(x => x == 7), 2);
    }

    // One LIMIT, at the very end of the command -- never inside the CTE that reads the parent documents
    private async Task limitShouldBeOnlyOnTheFlattenedRows<T>(Func<IQueryable<SelectManyHolder>, Task<T>> query,
        int expectedLimit)
    {
        await seed();

        await using var session = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;

        await query(session.Query<SelectManyHolder>());

        var (sql, parameters) = recorder.Commands.ShouldHaveSingleItem();

        Regex.Matches(sql, "LIMIT", RegexOptions.IgnoreCase).Count.ShouldBe(1, sql);
        var limit = Regex.Match(sql, @"LIMIT \$(\d+);?\s*$");
        limit.Success.ShouldBeTrue(sql);
        parameters[int.Parse(limit.Groups[1].Value) - 1].ShouldBe(expectedLimit, sql);
    }

    #endregion

    #region the shapes from the issue

    [Fact]
    public async Task first_over_child_documents_of_the_same_type()
    {
        // Target.Children is Target[], so a predicate meant for the children would also bind to the parent
        var parent = new Target { Number = 1, Children = [new Target { Number = 20 }, new Target { Number = 21 }] };
        var other = new Target { Number = 21, Children = [new Target { Number = 22 }] };
        theSession.Store(parent, other);
        await theSession.SaveChangesAsync();

        var first = await theSession.Query<Target>().SelectMany(x => x.Children).FirstAsync();
        new[] { 20, 21, 22 }.ShouldContain(first.Number);

        var hit = await theSession.Query<Target>().SelectMany(x => x.Children).FirstAsync(x => x.Number == 21);
        hit.Number.ShouldBe(21);
        hit.Id.ShouldBe(parent.Children[1].Id);

        var single = await theSession.Query<Target>().SelectMany(x => x.Children).SingleAsync(x => x.Number == 22);
        single.Id.ShouldBe(other.Children[0].Id);
    }

    [Fact]
    public async Task first_over_a_number_array()
    {
        theSession.Store(new Target { NumberArray = [] }, new Target { NumberArray = [5, 6] });
        await theSession.SaveChangesAsync();

        var first = await theSession.Query<Target>().SelectMany(x => x.NumberArray).FirstAsync();
        new[] { 5, 6 }.ShouldContain(first);

        (await theSession.Query<Target>().SelectMany(x => x.NumberArray).OrderByDescending(x => x).FirstOrDefaultAsync())
            .ShouldBe(6);
    }

    #endregion

    // Marten's OneResultHandler throws InvalidOperationException for both no-rows and too-many-rows,
    // the same type LINQ to Objects throws
    private static async Task shouldThrowLikeLinqToObjects<T>(Func<Task<T>> marten, Func<T> linqToObjects)
    {
        var expected = Should.Throw<InvalidOperationException>(() => linqToObjects());
        var actual = await Should.ThrowAsync<InvalidOperationException>(marten);

        actual.GetType().ShouldBe(expected.GetType());
    }
}

public class SelectManyHolder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SelectManyItem[] Items { get; set; } = [];
    public int[] Numbers { get; set; } = [];
}

public record SelectManyItem
{
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
}
