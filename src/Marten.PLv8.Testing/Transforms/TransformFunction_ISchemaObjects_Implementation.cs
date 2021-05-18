using System;
using System.Threading.Tasks;
using Marten.PLv8.Transforms;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.PLv8.Testing.Transforms
{
    [Collection("transforms")]
    public class TransformFunction_ISchemaObjects_Implementation : OneOffConfigurationsContext
    {
        public TransformFunction_ISchemaObjects_Implementation() : base("transforms")
        {
            StoreOptions(_ =>
            {
                _.UseJavascriptTransformsAndPatching(x => x.LoadFile("get_fullname.js"));
            });

            theStore.Tenancy.Default.EnsureStorageExists(typeof(TransformSchema));
        }

        [Fact]
        public async Task can_generate_when_the_transform_is_requested()
        {
            var transform = theStore.Tenancy.Default.TransformFor("get_fullname");

            var dbObjectNames = (await theStore.Tenancy.Default.Functions());
            dbObjectNames
                .ShouldContain(transform.Identifier);
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

                _.UseJavascriptTransformsAndPatching(x =>
                {
                    x.LoadJavascript("get_fullname", "module.exports = function(){return {};}");
                });

            });

            store2.Tenancy.Default.EnsureStorageExists(typeof(TransformSchema));

            var transform2 = store2.Tenancy.Default.TransformFor("get_fullname");


            SpecificationExtensions.ShouldContain((await store2.Tenancy.Default.DefinitionForFunction(transform2.Identifier))
                    .Body(), transform2.Body);
        }
    }
}
