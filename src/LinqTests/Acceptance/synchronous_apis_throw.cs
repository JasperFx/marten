using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Pagination;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

/// <summary>
///     #5861: every synchronous LINQ operator on a Marten queryable throws <see cref="NotSupportedException" />, and the
///     message says so for Marten 10 and names the asynchronous operator to use. The messages are spelled out here
///     rather than read from Marten, because the text is what users see and what the migration guide quotes.
/// </summary>
public class synchronous_apis_throw: IntegrationContext
{
    private const string EnumerationMessage =
        "Synchronous data access is not supported in Marten 10: enumerating a Marten query (foreach, ToList(), " +
        "ToArray(), ToDictionary(), ToHashSet() and the like) would query the database synchronously. Use await " +
        "ToListAsync() instead, or await foreach over ToAsyncEnumerable().";

    public synchronous_apis_throw(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private static string operatorMessage(string op, string replacement) =>
        $"Synchronous data access is not supported in Marten 10: {op}() would query the database synchronously. Use {replacement} instead.";

    [Fact]
    public void ToList_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().ToList());
        ex.Message.ShouldBe(EnumerationMessage);
    }

    [Fact]
    public void ToArray_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().ToArray());
        ex.Message.ShouldBe(EnumerationMessage);
    }

    [Fact]
    public void ToDictionary_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().ToDictionary(x => x.Id));
        ex.Message.ShouldBe(EnumerationMessage);
    }

    [Fact]
    public void ToHashSet_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().ToHashSet());
        ex.Message.ShouldBe(EnumerationMessage);
    }

    [Fact]
    public void ToList_after_composition_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() =>
            theSession.Query<Target>().Where(x => x.Number > 3).OrderBy(x => x.Number).Select(x => x.Id).ToList());
        ex.Message.ShouldBe(EnumerationMessage);
    }

    [Fact]
    public void foreach_iteration_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() =>
        {
            foreach (var _ in theSession.Query<User>())
            {
                // never reached
            }
        });
        ex.Message.ShouldBe(EnumerationMessage);
    }

    [Fact]
    public void non_generic_enumerator_throws_NotSupportedException()
    {
        var queryable = theSession.Query<User>();
        var ex = Should.Throw<NotSupportedException>(() => ((IEnumerable)queryable).GetEnumerator());
        ex.Message.ShouldBe(EnumerationMessage);
    }

    public static TheoryData<string, string> SingleResultOperators => new()
    {
        { "First", "await FirstAsync()" },
        { "FirstOrDefault", "await FirstOrDefaultAsync()" },
        { "Single", "await SingleAsync()" },
        { "SingleOrDefault", "await SingleOrDefaultAsync()" },
        { "Count", "await CountAsync()" },
        { "LongCount", "await LongCountAsync()" },
        { "Any", "await AnyAsync()" },
        { "FirstWithPredicate", "await FirstAsync()" },
        { "CountWithPredicate", "await CountAsync()" },
        { "AnyWithPredicate", "await AnyAsync()" },
        { "Min", "await MinAsync()" },
        { "Max", "await MaxAsync()" },
        { "Sum", "await SumAsync()" },
        { "Average", "await AverageAsync()" },
        { "All", "await AnyAsync() with the predicate negated" },
        { "Contains", "await AnyAsync() with an equality predicate" },
        { "Last", "await FirstAsync() with the ordering reversed" },
        { "LastOrDefault", "await FirstOrDefaultAsync() with the ordering reversed" },
        { "ElementAt", "Skip(index) and then await FirstAsync()" },
        { "ElementAtOrDefault", "Skip(index) and then await FirstOrDefaultAsync()" }
    };

    [Theory]
    [MemberData(nameof(SingleResultOperators))]
    public void single_result_operators_name_the_async_operator(string op, string replacement)
    {
        var targets = theSession.Query<Target>();
        Action execute = op switch
        {
            "First" => () => targets.First(),
            "FirstOrDefault" => () => targets.FirstOrDefault(),
            "Single" => () => targets.Single(),
            "SingleOrDefault" => () => targets.SingleOrDefault(),
            "Count" => () => targets.Count(),
            "LongCount" => () => targets.LongCount(),
            "Any" => () => targets.Any(),
            "FirstWithPredicate" => () => targets.First(x => x.Number == 3),
            "CountWithPredicate" => () => targets.Count(x => x.Number == 3),
            "AnyWithPredicate" => () => targets.Any(x => x.Number == 3),
            "Min" => () => targets.Min(x => x.Number),
            "Max" => () => targets.Max(x => x.Number),
            "Sum" => () => targets.Sum(x => x.Number),
            "Average" => () => targets.Average(x => x.Number),
            "All" => () => targets.All(x => x.Number > 0),
            "Contains" => () => targets.Select(x => x.Number).Contains(3),
            "Last" => () => targets.OrderBy(x => x.Number).Last(),
            "LastOrDefault" => () => targets.OrderBy(x => x.Number).LastOrDefault(),
            "ElementAt" => () => targets.OrderBy(x => x.Number).ElementAt(2),
            "ElementAtOrDefault" => () => targets.OrderBy(x => x.Number).ElementAtOrDefault(2),
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };

        var name = op.Replace("WithPredicate", "");

        var ex = Should.Throw<NotSupportedException>(execute);
        ex.Message.ShouldBe(operatorMessage(name, replacement));
    }

    [Fact]
    public void an_operator_without_an_async_counterpart_gets_the_generic_advice()
    {
        var ex = Should.Throw<NotSupportedException>(() =>
            theSession.Query<Target>().Select(x => x.Number).Aggregate((a, b) => a + b));

        ex.Message.ShouldBe(
            "Synchronous data access is not supported in Marten 10: Aggregate() would query the database synchronously. " +
            "Use the asynchronous operator instead: ToListAsync(), FirstAsync(), FirstOrDefaultAsync(), SingleAsync(), " +
            "CountAsync(), AnyAsync(), SumAsync(), MinAsync(), MaxAsync() or AverageAsync().");
    }

    [Fact]
    public void executing_an_expression_that_is_not_an_operator_gets_the_generic_advice()
    {
        var provider = theSession.Query<Target>().Provider;

        var ex = Should.Throw<NotSupportedException>(() => provider.Execute<int>(Expression.Constant(1)));

        ex.Message.ShouldStartWith("Synchronous data access is not supported in Marten 10: this LINQ operator");
        ex.Message.ShouldContain("ToListAsync()");
    }

    [Fact]
    public void ToPagedList_names_ToPagedListAsync()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<Target>().ToPagedList(1, 10));
        ex.Message.ShouldBe("Synchronous data access is not supported in Marten 10. Use await ToPagedListAsync() instead.");
    }

    [Fact]
    public void the_generic_message_references_marten_10()
    {
        QuerySession.SynchronousNotSupportedMessage
            .ShouldBe("Synchronous data access is not supported in Marten 10. Use the asynchronous equivalent.");
    }

    [Fact]
    public async Task composing_a_query_does_not_execute_it()
    {
        // Building the expression, sub-queries in predicates included, never reaches the provider's Execute or
        // the queryable's GetEnumerator. Only the terminal operator decides sync or async.
        var targets = Target.GenerateRandomData(20).ToArray();
        targets[0].Number = 5;
        targets[0].NumberArray = [1, 2, 3];
        targets[0].Children = [new Target { Number = 7 }];
        await theStore.BulkInsertDocumentsAsync(targets);

        var queryable = theSession.Query<Target>()
            .Where(x => x.NumberArray.Contains(3) && x.Children.Any(c => c.Number == 7))
            .Where(x => x.Id == targets[0].Id)
            .OrderBy(x => x.Number)
            .Take(5);

        queryable.ToCommand(FetchType.FetchMany).CommandText.ShouldNotBeNullOrEmpty();

        var results = await queryable.ToListAsync();
        results.Single().Id.ShouldBe(targets[0].Id);

        (await queryable.CountAsync()).ShouldBe(1);
        (await queryable.AnyAsync()).ShouldBeTrue();
        (await queryable.Select(x => x.Number).FirstAsync()).ShouldBe(5);
    }

    [Fact]
    public async Task compiled_queries_still_run()
    {
        var target = new Target { Number = 5, String = Guid.NewGuid().ToString() };
        await theStore.BulkInsertDocumentsAsync([target]);

        var found = await theSession.QueryAsync(new TargetByString { String = target.String });
        found.ShouldNotBeNull().Id.ShouldBe(target.Id);
    }

    public class TargetByString: ICompiledQuery<Target, Target>
    {
        public string String { get; set; }

        public Expression<Func<IMartenQueryable<Target>, Target>> QueryIs()
        {
            return q => q.FirstOrDefault(x => x.String == String);
        }
    }
}
