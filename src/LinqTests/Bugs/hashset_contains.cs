using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class hashset_contains: BugIntegrationContext
{
    [Fact(Skip = "TODO: Fix it on the linq branch")]
    public async Task Can_query_by_hashset_contains()
    {
        var value = Guid.NewGuid().ToString();
        theSession.Store(new MySpecialType(Guid.NewGuid(), value));
        await theSession.SaveChangesAsync();

        var hashset = new HashSet<string> { value };
        var items = await theSession
            .Query<MySpecialType>()
            .Where(x => hashset.Contains(x.Value))
            .ToListAsync();

        Assert.Single(items);
    }

    public record MySpecialType(Guid Id, string Value);
}
