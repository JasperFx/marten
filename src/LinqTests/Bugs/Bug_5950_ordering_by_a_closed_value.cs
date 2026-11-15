using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class ClosedOrderingDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int[] Values { get; set; } = [];
}

public static class ClosedOrderingProbe
{
    public static int Value = 3;
    public static string Name = "b";
    public static int[][] Jagged = [[1, 2], [3, 4]];
}

// #5950: ordering by a value that reads nothing from the document, OrderBy(x => Probe.Value), threw "Invalid
// OrderBy() expression". In LINQ to Objects such a key is the same for every element, and the stable sort leaves
// the order to the other keys, so a ThenBy() after it acts as the primary ordering. Marten now drops the key from
// the ORDER BY. Every ordering here is compared with LINQ to Objects over the same documents, in order.
public class Bug_5950_ordering_by_a_closed_value: IntegrationContext
{
    private static readonly ClosedOrderingDoc[] Docs = Enumerable.Range(1, 6).Select(i => new ClosedOrderingDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Name = new[] { "f", "c", "a", "e", "b", "d" }[i - 1],
        Category = i % 2 == 0 ? "even" : "odd",
        Values = [i, 7 - i]
    }).ToArray();

    public Bug_5950_ordering_by_a_closed_value(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(ClosedOrderingDoc)];

    protected override async Task fixtureSetup()
    {
        theSession.Store(Docs);
        await theSession.SaveChangesAsync();
    }

    public static IEnumerable<object[]> OrderingNames() => ClosedOrderings.All().Keys.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(OrderingNames))]
    public async Task ordering_matches_linq_to_objects(string name)
    {
        var order = ClosedOrderings.All()[name];
        var expected = order(Docs.AsQueryable()).Select(x => x.Number).ToList();

        await using var session = theStore.QuerySession();
        var query = order(session.Query<ClosedOrderingDoc>()).Select(x => x.Number);
        var actual = await query.ToListAsync();

        actual.ShouldBe(expected, query.ToCommand().CommandText);
    }

    [Fact]
    public async Task only_closed_keys_leave_no_order_by()
    {
        await using var session = theStore.QuerySession();
        var local = 5;

        var query = session.Query<ClosedOrderingDoc>().OrderBy(x => ClosedOrderingProbe.Value)
            .ThenByDescending(x => local).ThenBy(x => 1);

        query.ToCommand().CommandText.ShouldNotContain("order by", Case.Insensitive);

        var actual = await query.Select(x => x.Number).ToListAsync();
        actual.OrderBy(x => x).ShouldBe(Docs.Select(x => x.Number));
    }

    [Fact]
    public async Task a_closed_key_does_not_change_the_sql_of_the_other_keys()
    {
        await using var session = theStore.QuerySession();

        var withClosedKey = session.Query<ClosedOrderingDoc>().OrderBy(x => ClosedOrderingProbe.Value)
            .ThenByDescending(x => x.Name).ThenBy(x => ClosedOrderingProbe.Name).ThenBy(x => x.Number);
        var without = session.Query<ClosedOrderingDoc>().OrderByDescending(x => x.Name).ThenBy(x => x.Number);

        withClosedKey.ToCommand().CommandText.ShouldBe(without.ToCommand().CommandText);
    }

    [Fact]
    public async Task a_static_named_like_a_document_member_is_not_read_from_the_document()
    {
        // Before #5950 a plain static member key did not throw: MemberFinder took the lambda's parameter for the
        // start of the chain and resolved the static's NAME against the document. ClosedOrderingProbe.Name became
        // ORDER BY d.data ->> 'Name', silently ordering by the document's own Name.
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => ClosedOrderingProbe.Name).ThenByDescending(x => x.Number)
            .Select(x => x.Number).ToList();
        var actual = await session.Query<ClosedOrderingDoc>().OrderBy(x => ClosedOrderingProbe.Name)
            .ThenByDescending(x => x.Number).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task first_after_a_closed_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => ClosedOrderingProbe.Value).ThenByDescending(x => x.Number).First();
        var actual = await session.Query<ClosedOrderingDoc>().OrderBy(x => ClosedOrderingProbe.Value)
            .ThenByDescending(x => x.Number).FirstAsync();

        actual.Number.ShouldBe(expected.Number);
    }

    [Fact]
    public async Task paging_after_a_closed_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => ClosedOrderingProbe.Jagged[1][0]).ThenBy(x => x.Name).Skip(1).Take(3)
            .Select(x => x.Number).ToList();
        var actual = await session.Query<ClosedOrderingDoc>().OrderBy(x => ClosedOrderingProbe.Jagged[1][0])
            .ThenBy(x => x.Name).Skip(1).Take(3).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task a_closed_key_with_a_comparer()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => ClosedOrderingProbe.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name).Select(x => x.Number).ToList();
        var actual = await session.Query<ClosedOrderingDoc>()
            .OrderBy(x => ClosedOrderingProbe.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name)
            .Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task a_closed_key_in_a_compiled_query()
    {
        foreach (var min in new[] { 1, 3 })
        {
            var expected = Docs.Where(x => x.Number >= min).OrderBy(x => ClosedOrderingProbe.Value)
                .ThenByDescending(x => x.Name).Select(x => x.Number).ToList();

            await using var session = theStore.QuerySession();
            (await session.QueryAsync(new ClosedKeyCompiledQuery { Min = min })).ToList().ShouldBe(expected);
        }
    }

    [Fact]
    public async Task the_string_member_overload_is_still_an_ordering()
    {
        // OrderBy("Name") names the member as a string constant; it is not a closed key
        await using var session = theStore.QuerySession();

        var actual = await session.Query<ClosedOrderingDoc>().OrderBy("Name").Select(x => x.Number).ToListAsync();

        actual.ShouldBe(Docs.OrderBy(x => x.Name).Select(x => x.Number).ToList());
    }
}

public class ClosedKeyCompiledQuery: ICompiledListQuery<ClosedOrderingDoc, int>
{
    public int Min { get; set; }

    public Expression<Func<IMartenQueryable<ClosedOrderingDoc>, IEnumerable<int>>> QueryIs()
    {
        return q => q.Where(x => x.Number >= Min).OrderBy(x => ClosedOrderingProbe.Value).ThenByDescending(x => x.Name)
            .Select(x => x.Number);
    }
}

internal static class ClosedOrderings
{
    public static Dictionary<string, Func<IQueryable<ClosedOrderingDoc>, IQueryable<ClosedOrderingDoc>>> All()
    {
        var local = 7;
        var localName = "z";

        return new()
        {
            ["static_then_member"] = q => q.OrderBy(x => ClosedOrderingProbe.Value).ThenBy(x => x.Name),
            ["static_then_member_descending"] = q =>
                q.OrderByDescending(x => ClosedOrderingProbe.Value).ThenByDescending(x => x.Number),
            ["local_then_member"] = q => q.OrderBy(x => local).ThenByDescending(x => x.Name),
            ["literal_then_member"] = q => q.OrderBy(x => 1).ThenBy(x => x.Name),
            ["string_literal_then_member"] = q => q.OrderBy(x => "same").ThenByDescending(x => x.Name),
            ["indexed_static_then_member"] = q => q.OrderBy(x => ClosedOrderingProbe.Jagged[1][0]).ThenBy(x => x.Name),
            ["arithmetic_then_member"] = q => q.OrderBy(x => ClosedOrderingProbe.Value * 2 + local).ThenBy(x => x.Name),
            ["member_then_static"] = q =>
                q.OrderBy(x => x.Category).ThenBy(x => ClosedOrderingProbe.Value).ThenBy(x => x.Number),
            ["member_static_member"] = q =>
                q.OrderBy(x => x.Category).ThenByDescending(x => localName).ThenByDescending(x => x.Name),
            ["static_member_static_member"] = q => q.OrderBy(x => ClosedOrderingProbe.Name).ThenBy(x => x.Category)
                .ThenByDescending(x => ClosedOrderingProbe.Value).ThenByDescending(x => x.Number),
            ["static_then_document_index"] = q => q.OrderBy(x => ClosedOrderingProbe.Value).ThenBy(x => x.Values[1]),
            ["where_then_static_then_member"] = q =>
                q.Where(x => x.Number > 2).OrderBy(x => ClosedOrderingProbe.Value).ThenBy(x => x.Name)
        };
    }
}
