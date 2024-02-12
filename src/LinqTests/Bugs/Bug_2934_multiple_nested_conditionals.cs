using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2934_multiple_nested_conditionals : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public record NestedObject(Guid[] Guids, Guid[] MoreGuids, List<NestedObject> Obj);
    public record ObjectWithGuids(Guid Id, NestedObject NestedObject, string SomeText);

    public Bug_2934_multiple_nested_conditionals(ITestOutputHelper output)
    {
        _output = output;

        theSession.Logger = new TestOutputMartenLogger(_output);
    }

    [Fact]
    public async Task broken_linq_condition_1()
    {
        var array = new[] { Guid.NewGuid() };

        await theSession.Query<ObjectWithGuids>().Where(x =>
                !array.Any()
                || x.NestedObject.Guids.Any(z => array.Contains(z))
                || x.NestedObject.MoreGuids.Any(z => array.Contains(z)))
            .ToListAsync();
    }
}
