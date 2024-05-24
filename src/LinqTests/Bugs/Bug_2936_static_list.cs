using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2936_static_list : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public record NestedObject(Guid[] Guids, Guid[] MoreGuids, List<NestedObject> Obj);
    public record ObjectWithGuids(Guid Id, NestedObject NestedObject, string SomeText);

    public Bug_2936_static_list(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task broken_linq_condition_3()
    {
        theSession.Logger = new TestOutputMartenLogger(_output);
        await theSession.Query<ObjectWithGuids>().Where(x => SomeData.ConstantList.Contains(x.SomeText))
            .ToListAsync();
    }

    internal static class SomeData
    {
        public static readonly List<string> ConstantList = new List<string>() {"stuff"};
    }
}
