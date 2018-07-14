using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Schema
{
    public class SystemFunctionTests : IntegratedFixture
    {
        [Fact]
        public void generate_schema_objects_if_necessary()
        {
            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                conn.Execute(
                    cmd => cmd.Sql("drop function if exists public.mt_immutable_timestamptz(text)").ExecuteNonQuery());
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