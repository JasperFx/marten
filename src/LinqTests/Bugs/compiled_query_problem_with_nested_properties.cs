using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class compiled_query_problem_with_nested_properties: IntegrationContext
{
    [Fact]
    public async Task can_do_a_compiled_query_on_nested_property()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        var count = targets.Count(x => x.Inner.Number == 5);

        await using var session = theStore.QuerySession();
        var list = await session.QueryAsync(new CompiledNestedQuery { Number = 5 });
        list.Count().ShouldBe(count);
    }

    public compiled_query_problem_with_nested_properties(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class CompiledNestedQuery: ICompiledListQuery<Target>
{
    Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> ICompiledQuery<Target, IEnumerable<Target>>.QueryIs()
    {
        return q => q.Where(x => x.Inner.Number == Number);
    }

    public int Number { get; set; }
}
