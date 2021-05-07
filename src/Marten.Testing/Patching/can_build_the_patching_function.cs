using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Patching
{
    public class can_build_the_patching_function : IntegrationContext
    {
        [Fact]
        public async Task does_not_blow_up()
        {
            var transform = theStore.Tenancy.Default.TransformFor("patch_doc");

            (await theStore.Tenancy.Default.Functions())
                .ShouldContain(transform.Identifier);
        }

        public can_build_the_patching_function(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
