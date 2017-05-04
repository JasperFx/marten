using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Storage
{
    public class using_custom_ddl_rules_smoke_tests : IntegratedFixture
    {
        [Fact]
        public void can_use_CreateIfNotExists()
        {
            StoreOptions(_ =>
            {
                _.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;
            });

            // Would blow up if it doesn't work;)
            theStore.Tenants.Default.EnsureStorageExists(typeof(User));
        }


    }
}