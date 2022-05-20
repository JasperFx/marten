using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests
{
    public class adding_custom_schema_objects : OneOffConfigurationsContext
    {
        [Fact]
        public void extension_feature_is_not_active_without_any_extended_objects()
        {
            theStore.Options.Storage.AllActiveFeatures(theStore.Storage.Database)
                .OfType<StorageFeatures>().Any().ShouldBeFalse();
        }

        [Fact]
        public void extension_feature_is_active_with_custom_extended_objects()
        {
            var table = new Table("names");
            table.AddColumn<string>("name").AsPrimaryKey();
            theStore.Options.Storage.ExtendedSchemaObjects.Add(table);

            var feature = theStore.Options.Storage.AllActiveFeatures(theStore.Storage.Database)
                .OfType<StorageFeatures>().Single().As<IFeatureSchema>();

            feature.Objects.Single().ShouldBeTheSameAs(table);
        }

        [Fact]
        public async Task build_a_table()
        {
            // The schema is dropped when this method is called, so existing
            // tables would be dropped first
            StoreOptions(opts =>
            {
                opts.RegisterDocumentType<Target>();

                var table = new Table("adding_custom_schema_objects.names");
                table.AddColumn<string>("name").AsPrimaryKey();

                opts.Storage.ExtendedSchemaObjects.Add(table);
            });

            await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();


            using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync();

            var tableNames = await conn.ExistingTables(schemas: new[] { "adding_custom_schema_objects" });
            tableNames.Any(x => x.Name == "names").ShouldBeTrue();
        }
    }
}
