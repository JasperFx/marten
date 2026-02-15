using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
namespace LinqTests.Bugs;

public class Bug_2935_any_contains_in_child_collection : BugIntegrationContext
{
    public record NestedObject(Guid[] Guids, Guid[] MoreGuids, List<NestedObject> Obj);
    public record ObjectWithGuids(Guid Id, NestedObject NestedObject, string SomeText);

    [Fact]
    public async Task broken_linq_condition_2()
    {
        var id = Guid.NewGuid();

        await theSession.Query<ObjectWithGuids>().Where(x =>
                x.NestedObject.Obj.Any(obj => obj.Guids.Contains(id)))
            .ToListAsync();
    }
}
