using System;
using System.IO;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class ddl_generation_with_templates : IntegratedFixture
    {
        public ddl_generation_with_templates()
        {
            // I am neither proud nor ashamed of this code
            // Really need to put this in a helper somewhere because
            // it's coming up all over the place
            var directory = AppContext.BaseDirectory;
            while (!File.Exists(directory.AppendPath("project.json")))
            {
                directory = directory.ParentDirectory();
            }

            directory = directory.AppendPath("templates");

            StoreOptions(_ =>
            {
                _.DdlRules.ReadTemplates(directory);

                _.Schema.For<User>();
                _.Schema.For<BlueDoc>();
            });
        }

        [Fact]
        public void use_the_default_template_if_it_exists()
        {
            var ddl = theStore.Schema.ToDDL();
            ddl.ShouldContain("Default for public.mt_doc_user");
            ddl.ShouldContain("Default for public.mt_upsert_user");
        }

        [Fact]
        public void use_an_overridden_template_if_it_exists()
        {
            /*
-- Blue for public.mt_doc_bluedoc
-- Blue for public.mt_upsert_bluedoc
             */

            var ddl = theStore.Schema.ToDDL();
            ddl.ShouldContain("Blue for public.mt_doc_bluedoc");
            ddl.ShouldContain("Blue for public.mt_upsert_bluedoc");
        }

        [Fact]
        public void use_an_overridden_template_if_it_exists_via_fluent_interface()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
                _.Schema.For<BlueDoc>().DdlTemplate("blue");
            });

            theStore.Tenancy.Default.MappingFor(typeof(BlueDoc))
                .As<DocumentMapping>().DdlTemplate.ShouldBe("blue");
        }
    }

    // SAMPLE: configure_template_with_configure_marten
public class BlueDoc
{
    public static void ConfigureMarten(DocumentMapping mapping)
    {
        mapping.DdlTemplate = "blue";
    }

    public Guid id;
}
    // ENDSAMPLE

    // SAMPLE: configure_template_with_attribute
[DdlTemplate("ReadOnly")]
public class ReadOnlyDoc
{
    public Guid id;
}
    // ENDSAMPLE
}