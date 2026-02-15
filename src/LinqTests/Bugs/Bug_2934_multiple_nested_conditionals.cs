using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
namespace LinqTests.Bugs;

public class Bug_2934_multiple_nested_conditionals : BugIntegrationContext
{
    public record NestedObject(Guid[] Guids, Guid[] MoreGuids, List<NestedObject> Obj);
    public record ObjectWithGuids(Guid Id, NestedObject NestedObject, string SomeText);

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

    [Fact]
    public async Task Bug_3025_broken_linq_condition_2()
    {
        var parameterIds = new[] { Guid.NewGuid() };
        var guid = Guid.NewGuid();

        await theSession.Query<ObjectWithGuids>().Where(x =>
                !parameterIds.Any()
                || x.NestedObject.Guids.Any(z => parameterIds.Contains(z))
                && guid == x.Id)
            .ToListAsync();
    }
}
