using System.IO;
using System.Linq;
using Marten.Schema;
using Marten.Transforms;
using Shouldly;
using Xunit;

namespace Marten.Testing.Transforms
{
    public class JavascriptModule_storage_Tests : IntegratedFixture
    {
        [Fact]
        public void can_disable_javascript_projections_without_it_wigging_out()
        {
            var options = new StoreOptions();
            options.JavascriptProjectionsEnabled = true;
            options.JavascriptProjectionsEnabled = false;

            options.AllDocumentMappings.Any(x => x.DocumentType == typeof(JavascriptModule)).ShouldBeFalse();
        }

        [Fact]
        public void javascript_projections_are_not_enabled_by_default()
        {
            var storeOptions = new StoreOptions();
            storeOptions.JavascriptProjectionsEnabled.ShouldBeFalse();

            storeOptions.AllDocumentMappings.Any(x => x.DocumentType == typeof(JavascriptModule)).ShouldBeFalse();
        }


        [Fact]
        public void mapping_is_added_if_javascript_projections_are_enabled()
        {
            var options = new StoreOptions {JavascriptProjectionsEnabled = true};
            options.AllDocumentMappings.Any(x => x.DocumentType == typeof(JavascriptModule)).ShouldBeTrue();
        }

        [Fact]
        public void document_mapping_for_javascript_module_has_extra_script_dependencies()
        {
            var mapping = new DocumentMapping(typeof(JavascriptModule), new StoreOptions());

            mapping.DependentScripts.ShouldContain("mt_initialize_projections");
        }

        [Fact]
        public void build_the_javascript_module_storage_also_adds_dependent_scripts()
        {
            theStore.Schema.EnsureStorageExists(typeof(JavascriptModule));
            theStore.Schema.DbObjects.SchemaFunctionNames()
                .ShouldContain("mt_initialize_projections");
        }

        [Fact]
        public void javascript_module_mapping_writes_out_the_mt_initialize_projections_script_too()
        {
            var mapping = new DocumentMapping(typeof(JavascriptModule), new StoreOptions());

            var writer = new StringWriter();


            mapping.SchemaObjects.WriteSchemaObjects(theStore.Schema, writer);

            writer.ToString().ShouldContain("mt_initialize_projections");
        }
    }
}