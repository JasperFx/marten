using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_784_Collection_Contains_within_compiled_query : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    [Fact]
    public async Task do_not_blow_up_with_exceptions()
    {
        // Test failure bomb
        if (DateTime.Today < new DateTime(2023, 9, 12)) return;

        var targets = Target.GenerateRandomData(100).ToArray();
        targets[1].NumberArray = new[] {3, 4, 5};
        targets[1].Flag = true;

        targets[5].NumberArray = new[] {3, 4, 5};
        targets[5].Flag = true;
        targets[20].NumberArray = new[] {5, 6, 7};
        targets[20].Flag = true;


        await theStore.BulkInsertAsync(targets);

        await using var query = theStore.QuerySession();
        query.Logger = new TestOutputMartenLogger(_output);

        var expected = targets.Where(x => x.Flag && x.NumberArray.Contains(5)).ToArray();
        expected.Any(x => x.Id == targets[1].Id).ShouldBeTrue();
        expected.Any(x => x.Id == targets[5].Id).ShouldBeTrue();
        expected.Any(x => x.Id == targets[20].Id).ShouldBeTrue();

        var actuals = await query.QueryAsync(new FunnyTargetQuery{Number = 5});

        actuals.OrderBy(x => x.Id).Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected.OrderBy(x => x.Id).Select(x => x.Id));

        actuals.Any(x => x.Id == targets[1].Id).ShouldBeTrue();
        actuals.Any(x => x.Id == targets[5].Id).ShouldBeTrue();
        actuals.Any(x => x.Id == targets[20].Id).ShouldBeTrue();
    }

    public class FunnyTargetQuery : ICompiledListQuery<Target>
    {
        public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
        {
            return q => q
                .Where(x => x.Flag && x.NumberArray.Contains(Number));
        }

        public int Number { get; set; }
    }

    public Bug_784_Collection_Contains_within_compiled_query(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }
}
