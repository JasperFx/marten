using System.Threading.Tasks;
using Marten.Testing.Harness;
using Marten.Testing.OtherAssembly.Bug1851;

namespace LinqTests.Bugs;

public class Bug_1851_need_to_recursively_reference_assemblies_in_generic_type_parameters_of_compiled_queries : BugIntegrationContext
{
    [Fact]
    public async Task Should_be_able_execute_query()
    {
        _ = await theSession
            .QueryAsync(
                new GenericOuter<StoredObjectInThisAssembly>.FindByNameQuery { Name = "random" })
            .ConfigureAwait(false);
    }
}

public class StoredObjectInThisAssembly: StoredObjectInOtherAssembly
{

}