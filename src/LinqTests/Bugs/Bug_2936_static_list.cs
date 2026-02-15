using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
namespace LinqTests.Bugs;

public class Bug_2936_static_list : BugIntegrationContext
{
    public record NestedObject(Guid[] Guids, Guid[] MoreGuids, List<NestedObject> Obj);
    public record ObjectWithGuids(Guid Id, NestedObject NestedObject, string SomeText);

    [Fact]
    public async Task broken_linq_condition_3()
    {
        await theSession.Query<ObjectWithGuids>().Where(x => SomeData.ConstantList.Contains(x.SomeText))
            .ToListAsync();
    }

    internal static class SomeData
    {
        public static readonly List<string> ConstantList = new List<string>() {"stuff"};
    }
}
