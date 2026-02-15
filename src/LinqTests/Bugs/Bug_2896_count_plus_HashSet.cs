using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
namespace LinqTests.Bugs;

public class Bug_2896_count_plus_HashSet : BugIntegrationContext
{
    [Fact]
    public async Task try_to_query()
    {
        var guidList = new HashSet<Guid>() { Guid.NewGuid() };
        var count = await theSession
            .Query<User>()
            .Where(x=> guidList.Contains(x.Id))
            .CountAsync();
    }
}
