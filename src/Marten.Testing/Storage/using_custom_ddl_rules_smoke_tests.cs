using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Storage
{
    public class using_custom_ddl_rules_smoke_tests : IntegrationContext
    {
        [Fact]
        public void can_use_CreateIfNotExists()
        {
            StoreOptions(_ =>
            {
                _.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;
            });

            // Would blow up if it doesn't work;)
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
        }


        public using_custom_ddl_rules_smoke_tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
