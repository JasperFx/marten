using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_550_schema_diff_with_precision: BugIntegrationContext
    {

        [Fact]
        public async Task can_handle_the_explicit_precision()
        {
            // Configure a doc
            StoreOptions(_ =>
            {
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var store = SeparateStore(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            var patch = await store.Schema.CreateMigration(typeof(DocWithPrecision));
            patch.Difference.ShouldBe(SchemaPatchDifference.None);
        }


    }

    public class DocWithPrecision
    {
        public Guid Id;

        public string Name { get; set; }
    }
}
