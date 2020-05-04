using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    public class ApplyAllConfiguredChangesToDatabaseTests : IntegrationContext
    {
        [Fact]
        public void can_apply_schema_changes_independent_of_store_options_auto_create()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "apply_all_config_changes_to_db_0";
                _.Schema.For<User>();
                _.AutoCreateSchemaObjects = AutoCreate.None;
            });

            // should not throw SchemaValidationException
            Should.NotThrow(() =>
            {
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

        [Fact]
        public void can_apply_schema_changes_with_create_options_independent_of_store_options_auto_create()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "apply_all_config_changes_to_db_1";
                _.Schema.For<User>();
                _.AutoCreateSchemaObjects = AutoCreate.None;
            });

            // should not throw SchemaValidationException
            Should.NotThrow(() =>
            {
                theStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);
                theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

    }
}
