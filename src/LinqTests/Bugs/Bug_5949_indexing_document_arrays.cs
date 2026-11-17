using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class IndexedArrayDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Category { get; set; } = "";
    public int[] Values { get; set; } = [];
    public List<int> List { get; set; } = [];
    public string[] Names { get; set; } = [];
    public List<string> NameList { get; set; } = [];
    public bool[] Flags { get; set; } = [];
    public double[] Ratios { get; set; } = [];
    public int[][] Grid { get; set; } = [];
    public List<List<int>> Nested { get; set; } = [];
    public string[][] Words { get; set; } = [];
    public int[][][] Cube { get; set; } = [];
    public IndexedChild[] Children { get; set; } = [];
    public List<IndexedChild> ChildList { get; set; } = [];
    public IndexedChild[][] ChildGrid { get; set; } = [];
}

public record IndexedChild
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
}

public record IndexedProjection(int Number, int Cell, string Word);

// #5949: indexing an array (or a List<T>) on the document translated in a Where() comparison with a single index
// only. OrderBy(x => x.Values[1]) was refused, Select(x => x.Values[1]) threw ArgumentNullException ('array'), and
// any jagged index, x.Grid[1][0], threw too. Every case here is checked against LINQ to Objects over the same
// documents, with the index written as a literal and as a captured local.
public class Bug_5949_indexing_document_arrays: IntegrationContext
{
    private static readonly IndexedArrayDoc[] Docs = Enumerable.Range(1, 6).Select(i => new IndexedArrayDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Category = i % 2 == 0 ? "even" : "odd",
        Values = [i, 10 - i, i * 3],
        List = [i * 2, 7 - i, i],
        Names = ["n" + i, "m" + (7 - i)],
        NameList = ["p" + (7 - i), "q" + i],
        Flags = [i % 2 == 0, i > 3],
        Ratios = [i / 4.0, (7 - i) / 2.0],
        Grid = [[i, 7 - i], [i * 2, 10 - i]],
        Nested = [[7 - i, i], [i % 3, i]],
        Words = [["a" + i, "b" + (7 - i)], ["c" + i % 2, "d" + i]],
        Cube = [[[i, 1], [2, 7 - i]], [[i * 3, 4], [5, i % 4]]],
        Children = [new IndexedChild { Name = "c" + (7 - i), Score = i * 2 }, new IndexedChild { Name = "x" + i, Score = 10 - i }],
        ChildList = [new IndexedChild { Name = "y" + i, Score = 7 - i }, new IndexedChild { Name = "z" + (7 - i), Score = i }],
        ChildGrid = [[new IndexedChild { Name = "g" + i, Score = i }], [new IndexedChild { Name = "h" + (7 - i), Score = 7 - i }]]
    }).ToArray();

    public Bug_5949_indexing_document_arrays(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(IndexedArrayDoc)];

    protected override async Task fixtureSetup()
    {
        theSession.Store(Docs);
        await theSession.SaveChangesAsync();
    }

    #region Where

    public static IEnumerable<object[]> PredicateNames() => IndexedPredicates.All().Keys.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(PredicateNames))]
    public async Task where_matches_linq_to_objects(string name)
    {
        var predicate = IndexedPredicates.All()[name];
        var expected = Docs.AsQueryable().Where(predicate).Select(x => x.Number).OrderBy(x => x).ToList();

        // Every predicate selects something and leaves something out
        expected.ShouldNotBeEmpty(name);
        expected.Count.ShouldBeLessThan(Docs.Length, name);

        await using var session = theStore.QuerySession();
        var actual = await session.Query<IndexedArrayDoc>().Where(predicate).Select(x => x.Number).ToListAsync();

        actual.OrderBy(x => x).ToList().ShouldBe(expected, name);
    }

    [Fact]
    public async Task an_index_past_the_end_matches_nothing()
    {
        // LINQ to Objects throws IndexOutOfRangeException here; PostgreSQL reads a missing element as null
        await using var session = theStore.QuerySession();

        (await session.Query<IndexedArrayDoc>().Where(x => x.Values[7] == 3).ToListAsync()).ShouldBeEmpty();
        (await session.Query<IndexedArrayDoc>().Where(x => x.Grid[1][7] == 3).ToListAsync()).ShouldBeEmpty();
        (await session.Query<IndexedArrayDoc>().Where(x => x.Grid[7][0] == 3).ToListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task selecting_an_index_past_the_end_reads_null_or_default()
    {
        // The element is null in PostgreSQL; LINQ to Objects would throw IndexOutOfRangeException instead
        await using var session = theStore.QuerySession();

        (await session.Query<IndexedArrayDoc>().Select(x => x.Values[7]).ToListAsync()).ShouldAllBe(x => x == 0);
        (await session.Query<IndexedArrayDoc>().Select(x => x.Names[7]).ToListAsync()).ShouldAllBe(x => x == null);
        (await session.Query<IndexedArrayDoc>().Select(x => x.Grid[1][7]).ToListAsync()).ShouldAllBe(x => x == 0);
    }

    [Fact]
    public async Task a_negative_index_is_refused()
    {
        // PostgreSQL would read -1 as the last element, which is not what C# means by it
        await using var session = theStore.QuerySession();
        var index = -1;

        await Should.ThrowAsync<BadLinqExpressionException>(() =>
            session.Query<IndexedArrayDoc>().Where(x => x.Values[index] == 3).ToListAsync());
        await Should.ThrowAsync<BadLinqExpressionException>(() =>
            session.Query<IndexedArrayDoc>().Where(x => x.Grid[0][index] == 3).ToListAsync());
        await Should.ThrowAsync<BadLinqExpressionException>(() =>
            session.Query<IndexedArrayDoc>().OrderBy(x => x.Values[index]).ToListAsync());
        await Should.ThrowAsync<BadLinqExpressionException>(() =>
            session.Query<IndexedArrayDoc>().Select(x => x.List[index]).ToListAsync());
    }

    #endregion

    #region OrderBy

    private async Task orderingMatches<TKey>(Expression<Func<IndexedArrayDoc, TKey>> key, bool descending)
    {
        var expected = (descending ? Docs.AsQueryable().OrderByDescending(key) : Docs.AsQueryable().OrderBy(key))
            .ThenBy(x => x.Number).Select(x => x.Number).ToList();

        await using var session = theStore.QuerySession();
        var query = (descending
                ? session.Query<IndexedArrayDoc>().OrderByDescending(key)
                : session.Query<IndexedArrayDoc>().OrderBy(key))
            .ThenBy(x => x.Number).Select(x => x.Number);

        var actual = await query.ToListAsync();
        actual.ShouldBe(expected, query.ToCommand().CommandText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_value_array_index(bool descending) => orderingMatches(x => x.Values[1], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_value_array_index_from_a_local(bool descending)
    {
        var index = 2;
        return orderingMatches(x => x.Values[index], descending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_list_indexer(bool descending) => orderingMatches(x => x.List[1], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_element_at(bool descending) => orderingMatches(x => x.Values.ElementAt(2), descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_string_array_index(bool descending) => orderingMatches(x => x.Names[1], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_string_list_indexer(bool descending) => orderingMatches(x => x.NameList[0], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_double_array_index(bool descending) => orderingMatches(x => x.Ratios[1], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_jagged_array_index(bool descending) => orderingMatches(x => x.Grid[1][0], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_jagged_array_index_from_locals(bool descending)
    {
        var row = 0;
        var column = 1;
        return orderingMatches(x => x.Grid[row][column], descending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_nested_list_indexer(bool descending) => orderingMatches(x => x.Nested[1][0], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_jagged_string_array_index(bool descending) => orderingMatches(x => x.Words[0][1], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_three_deep_index(bool descending) => orderingMatches(x => x.Cube[1][1][1], descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_member_of_indexed_child(bool descending) => orderingMatches(x => x.Children[1].Score, descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_member_of_list_indexed_child(bool descending) => orderingMatches(x => x.ChildList[0].Name, descending);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task order_by_member_of_jagged_indexed_child(bool descending) =>
        orderingMatches(x => x.ChildGrid[1][0].Score, descending);

    [Fact]
    public async Task then_by_an_index()
    {
        var expected = Docs.OrderBy(x => x.Category).ThenByDescending(x => x.Grid[1][1]).Select(x => x.Number).ToList();

        await using var session = theStore.QuerySession();
        var actual = await session.Query<IndexedArrayDoc>().OrderBy(x => x.Category)
            .ThenByDescending(x => x.Grid[1][1]).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task order_by_an_index_past_the_end_sorts_the_missing_values_last()
    {
        // The value is null in PostgreSQL, and nulls sort last ascending; LINQ to Objects would throw
        await using var session = theStore.QuerySession();

        var actual = await session.Query<IndexedArrayDoc>().OrderBy(x => x.Values[7]).ThenBy(x => x.Number)
            .Select(x => x.Number).ToListAsync();

        actual.ShouldBe(Docs.Select(x => x.Number).OrderBy(x => x).ToList());
    }

    #endregion

    #region Select

    private async Task selectMatches<T>(Expression<Func<IndexedArrayDoc, T>> projection)
    {
        var expected = Docs.OrderBy(x => x.Number).AsQueryable().Select(projection).ToList();

        await using var session = theStore.QuerySession();
        var query = session.Query<IndexedArrayDoc>().OrderBy(x => x.Number).Select(projection);
        var actual = await query.ToListAsync();

        actual.ShouldBe(expected, query.ToCommand().CommandText);
    }

    [Fact]
    public Task select_value_array_index() => selectMatches(x => x.Values[1]);

    [Fact]
    public Task select_value_array_index_from_a_local()
    {
        var index = 2;
        return selectMatches(x => x.Values[index]);
    }

    [Fact]
    public Task select_list_indexer() => selectMatches(x => x.List[2]);

    [Fact]
    public Task select_element_at() => selectMatches(x => x.List.ElementAt(1));

    [Fact]
    public Task select_string_array_index() => selectMatches(x => x.Names[0]);

    [Fact]
    public Task select_string_list_indexer() => selectMatches(x => x.NameList[1]);

    [Fact]
    public Task select_bool_array_index() => selectMatches(x => x.Flags[1]);

    [Fact]
    public Task select_double_array_index() => selectMatches(x => x.Ratios[0]);

    [Fact]
    public Task select_jagged_array_index() => selectMatches(x => x.Grid[1][0]);

    [Fact]
    public Task select_jagged_array_index_from_locals()
    {
        var row = 1;
        var column = 1;
        return selectMatches(x => x.Grid[row][column]);
    }

    [Fact]
    public Task select_nested_list_indexer() => selectMatches(x => x.Nested[1][1]);

    [Fact]
    public Task select_jagged_string_array_index() => selectMatches(x => x.Words[1][0]);

    [Fact]
    public Task select_three_deep_index() => selectMatches(x => x.Cube[1][0][0]);

    [Fact]
    public Task select_member_of_indexed_child() => selectMatches(x => x.Children[1].Name);

    [Fact]
    public Task select_member_of_list_indexed_child() => selectMatches(x => x.ChildList[0].Score);

    [Fact]
    public Task select_member_of_jagged_indexed_child() => selectMatches(x => x.ChildGrid[1][0].Name);

    [Fact]
    public Task select_indexed_child() => selectMatches(x => x.Children[0]);

    [Fact]
    public Task select_indexed_row_of_a_jagged_array() => selectMatches(x => x.Grid[1]);

    [Fact]
    public Task select_indexes_into_an_anonymous_type() => selectMatches(x => new
    {
        x.Number,
        Value = x.Values[2],
        Listed = x.List[0],
        Cell = x.Grid[1][0],
        Nested = x.Nested[0][1],
        Word = x.Words[1][1],
        Deep = x.Cube[0][1][1],
        Child = x.Children[0].Name,
        Listed2 = x.ChildList[1].Score
    });

    [Fact]
    public Task select_indexes_into_a_record() =>
        selectMatches(x => new IndexedProjection(x.Number, x.Grid[0][1], x.Words[0][0]));

    #endregion
}

internal static class IndexedPredicates
{
    public static Dictionary<string, Expression<Func<IndexedArrayDoc, bool>>> All()
    {
        var one = 1;
        var zero = 0;

        return new()
        {
            ["value_array_literal"] = x => x.Values[1] == 7,
            ["value_array_local"] = x => x.Values[one] > 6,
            ["list_literal"] = x => x.List[1] == 4,
            ["list_local"] = x => x.List[zero] >= 8,
            ["element_at"] = x => x.Values.ElementAt(2) > 9,
            ["string_array"] = x => x.Names[1] == "m4",
            ["string_list"] = x => x.NameList[1] != "q2",
            ["double_array"] = x => x.Ratios[0] > 1.0,
            ["bool_array"] = x => x.Flags[1],
            ["not_bool_array"] = x => !x.Flags[0],
            ["bool_array_compared"] = x => x.Flags[0] == true && x.Number < 5,
            ["jagged_literal"] = x => x.Grid[1][0] == 4,
            ["jagged_locals"] = x => x.Grid[one][zero] > 6,
            ["jagged_not_equal"] = x => x.Grid[0][1] != 3,
            ["nested_list"] = x => x.Nested[1][0] == 0,
            ["jagged_string"] = x => x.Words[1][0] == "c1",
            ["three_deep"] = x => x.Cube[1][1][1] == 3,
            ["indexed_child_member"] = x => x.Children[1].Score > 6,
            ["list_indexed_child_member"] = x => x.ChildList[0].Name == "y3",
            ["jagged_indexed_child_member"] = x => x.ChildGrid[1][0].Score < 3,
            ["two_indexes_compared"] = x => x.Grid[0][0] < x.Values[1],
            ["jagged_and_member"] = x => x.Grid[1][1] >= 6 && x.Number != 2,
            ["jagged_or"] = x => x.Grid[0][0] == 1 || x.Words[0][1] == "b2",
            ["not_jagged"] = x => !(x.Grid[1][0] == 4),

            // A bool[] on the document could not be resolved at all before #5949
            ["bool_collection_contains"] = x => x.Flags.Contains(true) && x.Number < 4,
            ["bool_collection_any"] = x => x.Flags.Any(f => f) && x.Number > 2
        };
    }
}
