using Marten.Schema.Testing.Documents;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    public class using_custom_ddl_rules_smoke_tests : IntegrationContext
    {
        [Fact]
        public void can_use_CreateIfNotExists()
        {
            StoreOptions(_ =>
            {
                _.Advanced.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;
            });

            // Would blow up if it doesn't work;)
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
        }

    }
}
