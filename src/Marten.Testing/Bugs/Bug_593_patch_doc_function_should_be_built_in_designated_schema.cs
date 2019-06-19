using System.Linq;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_593_patch_doc_function_should_be_built_in_designated_schema: IntegratedFixture
    {
        [Fact]
        public void should_stick_the_patch_doc_function_in_the_right_schema()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var expected = new DbObjectName("other", "mt_transform_patch_doc");
            theStore.Tenancy.Default.DbObjects.Functions().Contains(expected).ShouldBeTrue();
        }
    }
}
