using System.Threading.Tasks;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing
{
    public class ApplyAllConfiguredChangesToDatabaseTests : IntegrationContext
    {
        [Fact]
        public async Task can_apply_schema_changes_independent_of_store_options_auto_create()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "apply_all_config_changes_to_db_0";
                _.Schema.For<User>();
                _.AutoCreateSchemaObjects = AutoCreate.None;
            });


            await Should.NotThrowAsync(async () =>
            {
                await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                await theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

        [Fact]
        public async Task can_apply_schema_changes_with_create_options_independent_of_store_options_auto_create()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "apply_all_config_changes_to_db_1";
                _.Schema.For<User>();
                _.AutoCreateSchemaObjects = AutoCreate.None;
            });

            await Should.NotThrowAsync(async () =>
            {
                await theStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);
                await theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

    }
}
