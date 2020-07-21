using Marten.Storage;
using Marten.Util;
using Npgsql;
using Xunit;

namespace Marten.Schema.Testing
{
    public class SystemFunctionTests : IntegrationContext
    {
        [Fact]
        public void generate_schema_objects_if_necessary()
        {
            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var cmd = new NpgsqlCommand("drop function if exists public.mt_immutable_timestamptz(text)");
                conn.Execute(cmd);
            }

            theStore.Tenancy.Default.DbObjects.DefinitionForFunction(new DbObjectName("public", "mt_immutable_timestamtzp"))
                .ShouldBeNull();

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();

            theStore.Tenancy.Default.EnsureStorageExists(typeof(SystemFunctions));

            theStore.Tenancy.Default.DbObjects.DefinitionForFunction(new DbObjectName("public", "mt_immutable_timestamptz"))
                .ShouldNotBeNull();
        }


    }
}
