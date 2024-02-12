using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2935_any_contains_in_child_collection : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public record NestedObject(Guid[] Guids, Guid[] MoreGuids, List<NestedObject> Obj);
    public record ObjectWithGuids(Guid Id, NestedObject NestedObject, string SomeText);

    public Bug_2935_any_contains_in_child_collection(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task broken_linq_condition_2()
    {
        var id = Guid.NewGuid();

        theSession.Logger = new TestOutputMartenLogger(_output);

        await theSession.Query<ObjectWithGuids>().Where(x =>
                x.NestedObject.Obj.Any(obj => obj.Guids.Contains(id)))
            .ToListAsync();
    }
}
