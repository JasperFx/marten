using System.Linq;
using System.Threading.Tasks;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_593_patch_doc_function_should_be_built_in_designated_schema: BugIntegrationContext
    {
        [Fact]
        public async Task should_stick_the_patch_doc_function_in_the_right_schema()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var expected = new DbObjectName("other", "mt_transform_patch_doc");
            (await theStore.Tenancy.Default.Functions()).Contains(expected).ShouldBeTrue();
        }

    }
}
