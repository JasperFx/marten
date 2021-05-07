using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Transforms
{
    public class TransformFunction_ISchemaObjects_Implementation : IntegrationContext
    {
        public TransformFunction_ISchemaObjects_Implementation(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(_ =>
            {
                _.Transforms.LoadFile("get_fullname.js");
            });
        }

        [Fact]
        public async Task can_generate_when_the_transform_is_requested()
        {
            var transform = theStore.Tenancy.Default.TransformFor("get_fullname");

            (await theStore.Tenancy.Default.Functions())
                .ShouldContain(transform.Identifier);
        }

        [Fact]
        public async Task reset_still_makes_it_check_again()
        {
            var transform = theStore.Tenancy.Default.TransformFor("get_fullname");

            theStore.Advanced.Clean.CompletelyRemoveAll();

            var transform2 = theStore.Tenancy.Default.TransformFor("get_fullname");

            (await theStore.Tenancy.Default.Functions())
                .ShouldContain(transform2.Identifier);
        }

        [Fact]
        public async Task regenerates_if_changed()
        {
            var transform = theStore.Tenancy.Default.TransformFor("get_fullname");

            (await theStore.Tenancy.Default.Functions())
                .ShouldContain(transform.Identifier);

            using var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.Transforms.LoadJavascript("get_fullname", "module.exports = function(){return {};}");
            });
            var transform2 = store2.Tenancy.Default.TransformFor("get_fullname");


            (await store2.Tenancy.Default.DefinitionForFunction(transform2.Identifier))
                .Body().ShouldContain(transform2.Body, Case.Sensitive);
        }
    }
}
