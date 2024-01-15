using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2896_count_plus_HashSet : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2896_count_plus_HashSet(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task try_to_query()
    {
        theSession.Logger = new TestOutputMartenLogger(_output);

        var guidList = new HashSet<Guid>() { Guid.NewGuid() };
        var count = await theSession
            .Query<User>()
            .Where(x=> guidList.Contains(x.Id))
            .CountAsync();
    }
}
