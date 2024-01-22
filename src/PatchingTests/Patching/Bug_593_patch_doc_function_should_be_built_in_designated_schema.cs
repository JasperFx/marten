using System.Linq;
using System.Threading.Tasks;
using Marten.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace PatchingTests.Patching;

public class Bug_593_patch_doc_function_should_be_built_in_designated_schema: BugIntegrationContext
{
    [Fact]
    public async Task should_stick_the_patch_doc_function_in_the_right_schema()
    {
        StoreOptions(_ =>
        {
            _.DatabaseSchemaName = "other";
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var expected = new PostgresqlObjectName("other", "mt_jsonb_patch");
        var functions = await theStore.Tenancy.Default.Database.Functions();
        functions.Contains(expected).ShouldBeTrue();
    }
}
