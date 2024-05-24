using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_1217_order_by_count_of_sub_collection : BugIntegrationContext
{
    [Fact]
    public async Task can_order_by_array_length()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        Expression<Func<Target, int>> expression = x => x.Children.Length;
        var memberInfos = MemberFinder.Determine(expression.Body);
        memberInfos.Length.ShouldBe(2);

        (await theSession.Query<Target>().OrderBy(x => x.Children.Length).ToListAsync()).ShouldNotBeNull();
    }


    [Fact]
    public async Task query_by_list_sub_collection_count()
    {
        // Just a smoke test here
        var list = await theSession.Query<TargetRoot>().OrderBy(x => x.ChildsLevel1.Count()).ToListAsync();
        list.ShouldNotBeNull();
    }
}

public class TargetRoot
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public ICollection<ChildLevel1> ChildsLevel1 { get; set; }
}

public class ChildLevel1
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public ICollection<ChildLevel2> ChildsLevel2 { get; set; }
}

public class ChildLevel2
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

