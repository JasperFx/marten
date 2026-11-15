using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Linq.Parsing;
using JasperFx;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class StaticProbeDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public int[] Values { get; set; } = [];
    public Dictionary<string, int> Scores { get; set; } = new();
}

public class StaticProbeConfig
{
    public int Limit { get; set; }
    public int[] Values { get; set; } = [];
    public StaticProbeConfig? Inner { get; set; }
}

// Everything the predicates below read that is not a document member. Every one of these is a constant as far as
// the query is concerned: Marten has to read its value on the client, not try to turn it into SQL.
public static class StaticProbe
{
    public const int Constant = 3;
    public const string ConstantName = "three";

    public static int Field = 3;
    public static readonly int ReadonlyField = 3;
    public static int Property { get; set; } = 3;
    public static string Name = "three";

    public static int[] Array = [1, 2, 3, 4];
    public static int[][] Jagged = [[1, 2], [3, 4]];
    public static int[,] Matrix = { { 1, 2 }, { 3, 4 } };
    public static string[] Names = ["one", "two", "three"];
    public static List<int> List = [1, 2, 3, 4];
    public static IList<int> InterfaceList = new List<int> { 1, 2, 3, 4 };
    public static Dictionary<string, int> Dictionary = new() { ["three"] = 3, ["one"] = 1 };
    public static IReadOnlyDictionary<int, string> NamesByNumber = new Dictionary<int, string> { [3] = "three" };

    public static StaticProbeConfig Config = new()
    {
        Limit = 3, Values = [1, 2, 3], Inner = new StaticProbeConfig { Limit = 2, Values = [5, 2] }
    };

    public static StaticProbeConfig ConfigProperty { get; } = new() { Limit = 3, Values = [3, 1] };
    public static List<StaticProbeConfig> Configs = [new() { Limit = 1 }, new() { Limit = 3 }];

    // The index Marten has to read before it can locate a member of the document
    public static int One = 1;
    public static string Key = "b";
    public static string[] Keys = ["a", "b"];
}

// #5943: a static member followed by an array index, `StaticProbe.Jagged[1][0]`, failed to parse with "Marten could
// not parse ... with the SimpleExpression construct" (inner ArgumentNullException 'array'). Copying the value into a
// local first worked. Every predicate here is checked against LINQ to Objects over the same documents, and the
// captured-local and instance-field shapes are controls for the static ones.
public class Bug_5943_static_members_inside_predicates: IntegrationContext
{
    private static readonly StaticProbeDoc[] Docs = Enumerable.Range(1, 6).Select(i => new StaticProbeDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Name = new[] { "one", "two", "three", "four", "five", "six" }[i - 1],
        Values = [i, i * 2, 7 - i],
        Scores = new Dictionary<string, int> { ["a"] = i, ["b"] = 7 - i }
    }).ToArray();

    public Bug_5943_static_members_inside_predicates(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(StaticProbeDoc)];

    protected override async Task fixtureSetup()
    {
        theSession.Store(Docs);
        await theSession.SaveChangesAsync();
    }

    public static IEnumerable<object[]> PredicateNames() =>
        new StaticProbePredicates().All().Keys.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(PredicateNames))]
    public async Task where_matches_linq_to_objects(string name)
    {
        var predicate = new StaticProbePredicates().All()[name];
        var expected = Docs.AsQueryable().Where(predicate).Select(x => x.Number).OrderBy(x => x).ToList();

        // Sanity: every predicate selects something and leaves something out, so neither an always-true nor an
        // always-false filter can pass
        expected.ShouldNotBeEmpty(name);
        expected.Count.ShouldBeLessThan(Docs.Length, name);

        await using var session = theStore.QuerySession();
        var actual = await session.Query<StaticProbeDoc>().Where(predicate).Select(x => x.Number).ToListAsync();

        actual.OrderBy(x => x).ToList().ShouldBe(expected, name);
    }

    [Fact]
    public async Task the_static_value_is_read_on_every_execution()
    {
        await using var session = theStore.QuerySession();
        var original = StaticProbe.Jagged[1][0];

        try
        {
            (await session.Query<StaticProbeDoc>().Where(x => x.Number == StaticProbe.Jagged[1][0])
                .Select(x => x.Number).ToListAsync()).ShouldBe([3]);

            StaticProbe.Jagged[1][0] = 5;

            (await session.Query<StaticProbeDoc>().Where(x => x.Number == StaticProbe.Jagged[1][0])
                .Select(x => x.Number).ToListAsync()).ShouldBe([5]);
        }
        finally
        {
            StaticProbe.Jagged[1][0] = original;
        }
    }

    [Fact]
    public async Task order_by_document_dictionary_by_static_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Scores[StaticProbe.Key]).Select(x => x.Number).ToList();
        var actual = await session.Query<StaticProbeDoc>().OrderBy(x => x.Scores[StaticProbe.Key])
            .Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task order_by_document_dictionary_by_static_array_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderByDescending(x => x.Scores[StaticProbe.Keys[1]]).Select(x => x.Number).ToList();
        var actual = await session.Query<StaticProbeDoc>().OrderByDescending(x => x.Scores[StaticProbe.Keys[1]])
            .Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task order_by_with_a_static_filter()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.Where(x => x.Number != StaticProbe.Jagged[1][0]).OrderByDescending(x => x.Number)
            .Select(x => x.Number).ToList();
        var actual = await session.Query<StaticProbeDoc>().Where(x => x.Number != StaticProbe.Jagged[1][0])
            .OrderByDescending(x => x.Number).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    public record StaticProjection(int Number, int Limit);

    [Fact]
    public async Task select_a_static_jagged_value_into_an_anonymous_type()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number).Select(x => new { x.Number, Limit = StaticProbe.Jagged[1][0] })
            .ToList();
        var actual = await session.Query<StaticProbeDoc>().OrderBy(x => x.Number)
            .Select(x => new { x.Number, Limit = StaticProbe.Jagged[1][0] }).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task select_static_values_into_a_record()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number)
            .Select(x => new StaticProjection(x.Number, StaticProbe.Config.Values[2] + StaticProbe.List[0])).ToList();
        var actual = await session.Query<StaticProbeDoc>().OrderBy(x => x.Number)
            .Select(x => new StaticProjection(x.Number, StaticProbe.Config.Values[2] + StaticProbe.List[0]))
            .ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task select_members_of_static_members()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number).Select(x => new
        {
            x.Number,
            Limit = StaticProbe.Config.Limit,
            Inner = StaticProbe.Config.Inner!.Values[0],
            Listed = StaticProbe.List[3],
            Keyed = StaticProbe.Dictionary["one"],
            Named = StaticProbe.Names[1],
            Field = StaticProbe.Field,
            Constant = StaticProbe.Constant
        }).ToList();

        var actual = await session.Query<StaticProbeDoc>().OrderBy(x => x.Number).Select(x => new
        {
            x.Number,
            Limit = StaticProbe.Config.Limit,
            Inner = StaticProbe.Config.Inner!.Values[0],
            Listed = StaticProbe.List[3],
            Keyed = StaticProbe.Dictionary["one"],
            Named = StaticProbe.Names[1],
            Field = StaticProbe.Field,
            Constant = StaticProbe.Constant
        }).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task select_a_bare_static_jagged_value()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.Where(x => x.Number < 3).Select(x => StaticProbe.Jagged[1][1]).ToList();
        var actual = await session.Query<StaticProbeDoc>().Where(x => x.Number < 3)
            .Select(x => StaticProbe.Jagged[1][1]).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task select_a_bare_static_field()
    {
        await using var session = theStore.QuerySession();

        var actual = await session.Query<StaticProbeDoc>().Where(x => x.Number < 3).Select(x => StaticProbe.Field)
            .ToListAsync();

        actual.ShouldBe([3, 3]);
    }

    [Fact]
    public async Task select_a_bare_captured_local()
    {
        await using var session = theStore.QuerySession();
        var local = 3;

        var actual = await session.Query<StaticProbeDoc>().Where(x => x.Number < 3).Select(x => local)
            .ToListAsync();

        actual.ShouldBe([3, 3]);
    }

    [Fact]
    public async Task static_values_in_a_compiled_query_behave_like_a_plain_static_field()
    {
        // A static is a constant to a compiled query, not one of its parameters. The plain static field has
        // always worked, so the indexed forms have to behave exactly as it does: across executions with
        // different parameter values, and after the static itself changes.
        var originalField = StaticProbe.Field;
        var originalJagged = StaticProbe.Jagged[1][0];
        var originalList = StaticProbe.List[2];

        try
        {
            foreach (var min in new[] { 1, 4, 2 })
            {
                var expected = Docs.Where(x => x.Number != StaticProbe.Field && x.Number >= min)
                    .Select(x => x.Number).OrderBy(x => x).ToList();

                await using var session = theStore.QuerySession();
                (await session.QueryAsync(new StaticFieldCompiledQuery { Min = min })).ToList().ShouldBe(expected);
                (await session.QueryAsync(new StaticJaggedCompiledQuery { Min = min })).ToList().ShouldBe(expected);
                (await session.QueryAsync(new StaticListCompiledQuery { Min = min })).ToList().ShouldBe(expected);
            }

            StaticProbe.Field = 5;
            StaticProbe.Jagged[1][0] = 5;
            StaticProbe.List[2] = 5;

            await using (var session = theStore.QuerySession())
            {
                var plain = (await session.QueryAsync(new StaticFieldCompiledQuery { Min = 1 })).ToList();
                (await session.QueryAsync(new StaticJaggedCompiledQuery { Min = 1 })).ToList().ShouldBe(plain);
                (await session.QueryAsync(new StaticListCompiledQuery { Min = 1 })).ToList().ShouldBe(plain);
            }
        }
        finally
        {
            StaticProbe.Field = originalField;
            StaticProbe.Jagged[1][0] = originalJagged;
            StaticProbe.List[2] = originalList;
        }
    }

    [Fact]
    public async Task static_inside_any_over_a_value_collection()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.Where(x => x.Values.Any(v => v == StaticProbe.Jagged[1][1])).Select(x => x.Number)
            .OrderBy(x => x).ToList();
        expected.ShouldNotBeEmpty();

        var actual = await session.Query<StaticProbeDoc>().Where(x => x.Values.Any(v => v == StaticProbe.Jagged[1][1]))
            .Select(x => x.Number).ToListAsync();

        actual.OrderBy(x => x).ToList().ShouldBe(expected);
    }

    [Fact]
    public async Task static_inside_contains_over_a_value_collection()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.Where(x => x.Values.Contains(StaticProbe.Jagged[1][1])).Select(x => x.Number)
            .OrderBy(x => x).ToList();
        expected.ShouldNotBeEmpty();

        var actual = await session.Query<StaticProbeDoc>().Where(x => x.Values.Contains(StaticProbe.Jagged[1][1]))
            .Select(x => x.Number).ToListAsync();

        actual.OrderBy(x => x).ToList().ShouldBe(expected);
    }
}

// The predicates live on their own class so MemberData can list them, and so the instance-field controls read
// fields of a real instance: Member(Constant(this), field)
internal class StaticProbePredicates
{
    // Instance fields reach the predicate as Member(Constant(this), ...), the same shape as a
    // captured local, and are controls for the static shapes
    private readonly int[][] _jagged = [[1, 2], [3, 4]];
    private readonly List<int> _list = [1, 2, 3, 4];
    private readonly Dictionary<string, int> _dictionary = new() { ["three"] = 3 };
    private readonly StaticProbeConfig _config = new() { Limit = 3, Values = [1, 2, 3] };
    private readonly int _one = 1;

    public Dictionary<string, Expression<Func<StaticProbeDoc, bool>>> All()
    {
        // Captured locals, as controls
        var localJagged = new[] { new[] { 1, 2 }, new[] { 3, 4 } };
        var localList = new List<int> { 1, 2, 3, 4 };
        var localDictionary = new Dictionary<string, int> { ["three"] = 3 };
        var localConfig = new StaticProbeConfig { Limit = 3, Values = [1, 2, 3] };
        var localValue = StaticProbe.Jagged[1][0];
        var localOne = 1;

        return new()
        {
            // the issue
            ["static_jagged_not_equal"] = x => x.Number != StaticProbe.Jagged[1][0],
            ["static_jagged_equal"] = x => x.Number == StaticProbe.Jagged[1][0],
            ["static_jagged_on_left"] = x => StaticProbe.Jagged[1][0] < x.Number,
            ["static_jagged_greater"] = x => x.Number > StaticProbe.Jagged[0][1],

            // plain static members
            ["static_field"] = x => x.Number == StaticProbe.Field,
            ["static_readonly_field"] = x => x.Number == StaticProbe.ReadonlyField,
            ["static_property"] = x => x.Number >= StaticProbe.Property,
            ["constant"] = x => x.Number == StaticProbe.Constant,
            ["constant_string"] = x => x.Name == StaticProbe.ConstantName,
            ["static_string"] = x => x.Name != StaticProbe.Name,

            // indexing into a static
            ["static_array"] = x => x.Number == StaticProbe.Array[2],
            ["static_array_length"] = x => x.Number == StaticProbe.Array.Length,
            ["static_string_array"] = x => x.Name == StaticProbe.Names[2],
            ["static_matrix"] = x => x.Number == StaticProbe.Matrix[1, 0],
            ["static_list"] = x => x.Number == StaticProbe.List[2],
            ["static_list_not_equal"] = x => x.Number != StaticProbe.List[2],
            ["static_interface_list"] = x => x.Number < StaticProbe.InterfaceList[2],
            ["static_list_count"] = x => x.Number == StaticProbe.List.Count,
            ["static_dictionary"] = x => x.Number == StaticProbe.Dictionary["three"],
            ["static_dictionary_by_constant_key"] = x => x.Number == StaticProbe.Dictionary[StaticProbe.ConstantName],
            ["static_readonly_dictionary"] = x => x.Name == StaticProbe.NamesByNumber[3],
            ["static_list_element_member"] = x => x.Number == StaticProbe.Configs[1].Limit,

            // members of static members
            ["static_member_of_static"] = x => x.Number == StaticProbe.Config.Limit,
            ["static_member_of_static_property"] = x => x.Number == StaticProbe.ConfigProperty.Limit,
            ["static_nested_member"] = x => x.Number == StaticProbe.Config.Inner!.Limit,
            ["static_member_array"] = x => x.Number == StaticProbe.Config.Values[2],
            ["static_nested_member_array"] = x => x.Number == StaticProbe.Config.Inner!.Values[1],
            ["static_member_of_static_property_array"] = x => x.Number == StaticProbe.ConfigProperty.Values[0],

            // a static in an index or arithmetic
            ["static_index_into_static"] = x => x.Number == StaticProbe.Array[StaticProbe.One],
            ["static_jagged_by_static_index"] = x => x.Number == StaticProbe.Jagged[StaticProbe.One][0],
            ["static_arithmetic"] = x => x.Number == StaticProbe.Jagged[1][0] + 1,

            // a static locates a member of the document
            ["document_array_by_static_index"] = x => x.Values[StaticProbe.One] == 4,
            ["document_array_by_jagged_index"] = x => x.Values[StaticProbe.Jagged[0][0]] == 4,
            ["document_dictionary_by_static_key"] = x => x.Scores[StaticProbe.Key] == 4,
            ["document_array_against_static"] = x => x.Values[0] == StaticProbe.Jagged[1][0],

            // a static collection searched for a document member
            ["static_jagged_row_contains"] = x => StaticProbe.Jagged[1].Contains(x.Number),
            ["static_list_element_contains"] = x => StaticProbe.Configs[1].Values.Contains(x.Number) || x.Number == 6,
            ["static_closed_contains_and_member"] = x => StaticProbe.List.Contains(3) && x.Number > 4,
            ["static_closed_contains_or_member"] = x => StaticProbe.List.Contains(99) || x.Number == 2,
            ["not_static_closed_contains"] = x => !StaticProbe.Names.Contains("zero") && x.Number < 3,

            // a static inside a predicate over a child collection
            ["closed_any_predicate_true"] = x => x.Values.Any(v => StaticProbe.List.Contains(3)) && x.Number < 3,
            ["closed_any_predicate_false"] = x => x.Values.Any(v => StaticProbe.List.Contains(99)) || x.Number == 1,
            ["closed_any_predicate_indexed"] = x => x.Values.Any(v => StaticProbe.Jagged[1][0] == 4) || x.Number == 1,
            ["closed_contains_and_element"] = x => x.Values.Any(v => StaticProbe.List.Contains(3) && v > 10),
            ["closed_comparison_and_element"] = x => x.Values.Any(v => StaticProbe.Jagged[1][0] == 3 && v > 10),
            ["static_compared_to_element"] = x => x.Values.Any(v => v == StaticProbe.Jagged[1][1]),

            // combined
            ["static_and_static"] = x => x.Number >= StaticProbe.Jagged[0][1] && x.Number <= StaticProbe.List[3],
            ["static_or_static"] = x => x.Number == StaticProbe.Jagged[0][0] || x.Name == StaticProbe.Names[2],
            ["not_static"] = x => !(x.Number == StaticProbe.Jagged[1][0]),

            // controls: captured locals
            ["local_value"] = x => x.Number != localValue,
            ["local_jagged"] = x => x.Number != localJagged[1][0],
            ["local_list"] = x => x.Number == localList[2],
            ["local_dictionary"] = x => x.Number == localDictionary["three"],
            ["local_member"] = x => x.Number == localConfig.Limit,
            ["local_member_array"] = x => x.Number == localConfig.Values[2],
            ["document_array_by_local_index"] = x => x.Values[localOne] == 4,

            // controls: instance fields of the test class
            ["instance_jagged"] = x => x.Number != _jagged[1][0],
            ["instance_list"] = x => x.Number == _list[2],
            ["instance_dictionary"] = x => x.Number == _dictionary["three"],
            ["instance_member"] = x => x.Number == _config.Limit,
            ["instance_member_array"] = x => x.Number == _config.Values[2],
            ["document_array_by_instance_index"] = x => x.Values[_one] == 4,
        };
    }
}

public class StaticFieldCompiledQuery: ICompiledListQuery<StaticProbeDoc, int>
{
    public int Min { get; set; }

    public Expression<Func<IMartenQueryable<StaticProbeDoc>, IEnumerable<int>>> QueryIs()
    {
        return q => q.Where(x => x.Number != StaticProbe.Field && x.Number >= Min).OrderBy(x => x.Number)
            .Select(x => x.Number);
    }
}

public class StaticJaggedCompiledQuery: ICompiledListQuery<StaticProbeDoc, int>
{
    public int Min { get; set; }

    public Expression<Func<IMartenQueryable<StaticProbeDoc>, IEnumerable<int>>> QueryIs()
    {
        return q => q.Where(x => x.Number != StaticProbe.Jagged[1][0] && x.Number >= Min).OrderBy(x => x.Number)
            .Select(x => x.Number);
    }
}

public class StaticListCompiledQuery: ICompiledListQuery<StaticProbeDoc, int>
{
    public int Min { get; set; }

    public Expression<Func<IMartenQueryable<StaticProbeDoc>, IEnumerable<int>>> QueryIs()
    {
        return q => q.Where(x => x.Number != StaticProbe.List[2] && x.Number >= Min).OrderBy(x => x.Number)
            .Select(x => x.Number);
    }
}

// #5943: the indexing shapes are evaluated by reflection, like the member reads #5328 covers, so they need no
// emitted or interpreted delegate. Each value is checked against what the compiled expression gives.
public class Bug_5943_static_indexing_evaluates_without_compiling
{
    public static IEnumerable<object[]> Shapes() =>
    [
        ["array index", (Expression<Func<object>>)(() => StaticProbe.Array[2])],
        ["jagged array index", (Expression<Func<object>>)(() => StaticProbe.Jagged[1][0])],
        ["jagged by a static index", (Expression<Func<object>>)(() => StaticProbe.Jagged[StaticProbe.One][1])],
        ["array length", (Expression<Func<object>>)(() => StaticProbe.Array.Length)],
        ["multi-dimensional array", (Expression<Func<object>>)(() => StaticProbe.Matrix[1, 0])],
        ["list indexer", (Expression<Func<object>>)(() => StaticProbe.List[2])],
        ["interface list indexer", (Expression<Func<object>>)(() => StaticProbe.InterfaceList[3])],
        ["dictionary indexer", (Expression<Func<object>>)(() => StaticProbe.Dictionary["three"])],
        ["read-only dictionary indexer", (Expression<Func<object>>)(() => StaticProbe.NamesByNumber[3])],
        ["member of an indexed element", (Expression<Func<object>>)(() => StaticProbe.Configs[1].Limit)],
        ["array of a member of a static", (Expression<Func<object>>)(() => StaticProbe.Config.Inner!.Values[1])]
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void evaluates_without_compiling(string shape, Expression<Func<object>> expression)
    {
        // The lambda boxes a value-typed body; the value read is the body underneath
        var body = expression.Body is UnaryExpression { NodeType: ExpressionType.Convert } box
            ? box.Operand
            : expression.Body;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(body, out var value).ShouldBeTrue(shape);
        value.ShouldBe(expression.Compile()(), shape);
    }

    [Fact]
    public void a_missing_key_is_reported_as_a_bad_linq_expression()
    {
        Expression<Func<int>> expression = () => StaticProbe.Dictionary["missing"];

        var ex = Should.Throw<BadLinqExpressionException>(() => expression.Body.ReduceToConstant());
        ex.InnerException.ShouldBeOfType<KeyNotFoundException>();
    }

    [Fact]
    public void an_index_out_of_range_is_reported_as_a_bad_linq_expression()
    {
        Expression<Func<int>> expression = () => StaticProbe.Jagged[1][7];

        var ex = Should.Throw<BadLinqExpressionException>(() => expression.Body.ReduceToConstant());
        ex.InnerException.ShouldBeOfType<IndexOutOfRangeException>();
    }
}
