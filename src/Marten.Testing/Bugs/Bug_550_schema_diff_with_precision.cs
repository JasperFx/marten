using System;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_550_schema_diff_with_precision: BugIntegrationContext
    {

        [Fact]
        public void can_handle_the_explicit_precision()
        {
            // Configure a doc
            StoreOptions(_ =>
            {
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var store = SeparateStore(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            var patch = store.Schema.ToPatch(typeof(DocWithPrecision));
            patch.Difference.ShouldBe(SchemaPatchDifference.None);
        }


    }

    public class DocWithPrecision
    {
        public Guid Id;

        public string Name { get; set; }
    }
}
