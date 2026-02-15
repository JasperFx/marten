using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Bugs;

public class Bug_3030_nested_array_queries : BugIntegrationContext
{
    public sealed record NestedObject2(List<Guid> MyPileOfGuids);
    public sealed record NestedObject1(List<NestedObject2> NestedObject2s);
    public sealed record RootObject(Guid Id, NestedObject1 NestedObject);

    [Fact]
    public async Task Bug_nested_array_querying()
    {
        var searchGuid = Guid.NewGuid();

        var entity = new RootObject(Guid.NewGuid(),
            new NestedObject1([new([searchGuid])]));

        theSession.Store(entity);
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<RootObject>()
            .Where(x => x.NestedObject.NestedObject2s.Any(y => y.MyPileOfGuids.Contains(searchGuid)))
            .SingleOrDefaultAsync();

        result.ShouldNotBeNull();
    }
}
