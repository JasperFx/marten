using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Patching
{
    public class can_build_the_patching_function : IntegrationContext
    {
        [Fact]
        public void does_not_blow_up()
        {
            var transform = theStore.Tenancy.Default.TransformFor("patch_doc");

            theStore.Tenancy.Default.DbObjects.Functions()
                .ShouldContain(transform.Identifier);
        }

        public can_build_the_patching_function(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
