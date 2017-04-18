using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Transforms
{
    public class TransformFunction_ISchemaObjects_Implementation : IntegratedFixture
    {
        public TransformFunction_ISchemaObjects_Implementation()
        {
            StoreOptions(_ =>
            {
                _.Transforms.LoadFile("get_fullname.js");
            });
        }

        [Fact]
        public void can_generate_when_the_transform_is_requested()
        {
            var transform = theStore.DefaultTenant.TransformFor("get_fullname");

            theStore.Schema.DbObjects.SchemaDbObjectNames()
                .ShouldContain(transform.Identifier);
        }

        [Fact]
        public void reset_still_makes_it_check_again()
        {
            var transform = theStore.DefaultTenant.TransformFor("get_fullname");

            theStore.Advanced.Clean.CompletelyRemoveAll();

            var transform2 = theStore.DefaultTenant.TransformFor("get_fullname");

            theStore.Schema.DbObjects.SchemaDbObjectNames()
                .ShouldContain(transform2.Identifier);
        }

        [Fact]
        public void patch_if_it_does_not_exist()
        {
            var patch = new SchemaPatch(new DdlRules());

            theStore.Advanced.Options.Transforms.For("get_fullname")
                .WritePatch(theStore.Schema, patch);

            patch.UpdateDDL.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_transform_get_fullname(doc JSONB) RETURNS JSONB AS $$");
        }

        [Fact]
        public void no_patch_if_it_does_exist()
        {
            var transform = theStore.DefaultTenant.TransformFor("get_fullname");

            var patch = new SchemaPatch(new DdlRules());

            theStore.Advanced.Options.Transforms.For("get_fullname")
                .WritePatch(theStore.Schema, patch);

            patch.UpdateDDL.ShouldNotContain("CREATE OR REPLACE FUNCTION public.mt_transform_get_fullname(doc JSONB) RETURNS JSONB AS $$");
        }

        [Fact]
        public void regenerates_if_changed()
        {
            var transform = theStore.DefaultTenant.TransformFor("get_fullname");

            theStore.Schema.DbObjects.SchemaDbObjectNames()
                .ShouldContain(transform.Identifier);

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.Transforms.LoadJavascript("get_fullname", "module.exports = function(){return {};}");
            }))
            {
                var transform2 = store2.DefaultTenant.TransformFor("get_fullname");


                store2.Schema.DbObjects.DefinitionForFunction(transform2.Identifier)
                    .Body
                    .ShouldContain(transform2.Body);
            }
        }
    }
}