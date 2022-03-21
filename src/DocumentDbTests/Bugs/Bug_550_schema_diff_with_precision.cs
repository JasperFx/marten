using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs
{
    [Obsolete("Move to Weasel")]
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

            await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            var store = SeparateStore(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            var feature = store.Storage.Database.BuildFeatureSchemas()
                .FirstOrDefault(x => x.StorageType == typeof(DocWithPrecision));
            var patch = await store.Storage.Database.CreateMigrationAsync(feature);
            patch.Difference.ShouldBe(SchemaPatchDifference.None);
        }


    }

    public class DocWithPrecision
    {
        public Guid Id;

        public string Name { get; set; }
    }
}
