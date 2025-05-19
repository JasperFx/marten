using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

public class Bug_605_unary_expressions_in_where_clause_of_compiled_query: BugIntegrationContext
{
    [Fact]
    public async Task with_flag_as_true()
    {
        var targets = Target.GenerateRandomData(1000).ToArray();
        await theStore.BulkInsertAsync(targets);

        await using var query = theStore.QuerySession();
        var results = await query.QueryAsync(new FlaggedTrueTargets());

        var expected = query.Query<Target>()
            .SelectMany(x => x.Children)
            .Where(x => x.Color == Colors.Green)
            .Where(x => x.Flag)
            .OrderBy(x => x.Id)
            .Skip(20)
            .Take(15)
            .ToList();

        results.Count().ShouldBe(15);

        results.Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected.Select(x => x.Id));
    }

    [Fact]
    public async Task with_flag_as_true_with_enum_as_string()
    {
        StoreOptions(_ => _.UseSystemTextJsonForSerialization(EnumStorage.AsString));

        var targets = Target.GenerateRandomData(1000).ToArray();
        await theStore.BulkInsertAsync(targets);

        await using var query = theStore.QuerySession();
        var results = await query.QueryAsync(new FlaggedTrueTargets());

        var expected = query.Query<Target>()
            .SelectMany(x => x.Children)
            .Where(x => x.Color == Colors.Green)
            .Where(x => x.Flag)
            .OrderBy(x => x.Id)
            .Skip(20)
            .Take(15)
            .ToList();

        results.Count().ShouldBe(15);

        results.Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected.Select(x => x.Id));
    }

    [Fact]
    public async Task with_flag_as_false()
    {
        var targets = Target.GenerateRandomData(1000).ToArray();
        await theStore.BulkInsertAsync(targets);

        await using var query = theStore.QuerySession();
        var results = await query.QueryAsync(new FlaggedFalseTargets());

        var expected = query.Query<Target>()
            .SelectMany(x => x.Children)
            .Where(x => x.Color == Colors.Green)
            .Where(x => !x.Flag)
            .OrderBy(x => x.Id)
            .Skip(20)
            .Take(15)
            .ToList();

        results.Count().ShouldBe(15);

        results.Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected.Select(x => x.Id));
    }

    public class FlaggedTrueTargets: ICompiledListQuery<Target>
    {
        public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
        {
            return q => q.SelectMany(x => x.Children)
                .Where(x => x.Color == Color)
                .Where(x => x.Flag)
                .OrderBy(x => x.Id)
                .Skip(Skip)
                .Take(Take);
        }

        public Colors Color { get; set; } = Colors.Green;

        public int Skip { get; set; } = 20;
        public int Take { get; set; } = 15;
    }

    public class FlaggedFalseTargets: ICompiledListQuery<Target>
    {
        public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
        {
            return q => q.SelectMany(x => x.Children)
                .Where(x => x.Color == Colors.Green)
                .Where(x => !x.Flag)
                .OrderBy(x => x.Id)
                .Skip(Skip)
                .Take(Take);
        }

        public int Skip { get; set; } = 20;
        public int Take { get; set; } = 15;
    }


}
