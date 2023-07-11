using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class Bug_2282_SOE_on_hiearchical_usage : BugIntegrationContext
{
    [Fact]
    public async Task do_not_throw_SOE()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<BaseClass>().AddSubClassHierarchy();
        });

        var data = await theSession.Query<BaseClass>()
            .Where(x => x.OtherId == Guid.NewGuid())
            .ToListAsync();

    }
}

public class BaseClass
{
    public Guid Id { get; set; }
    public Guid OtherId { get; set; }
    public List<string> ListProp { get; set; }
}

public class Subclass1 : BaseClass
{
    public string Test2 { get; set; }
    public int Test3 { get; set; }
    public DateTimeOffset? Test4 { get; set; }
}

public class Subclass2 : BaseClass
{
    public IDictionary<string, int> Test5 { get; set; }
    public bool Test6 { get; set; }
}
