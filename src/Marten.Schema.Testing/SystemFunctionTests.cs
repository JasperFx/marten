using System.Threading.Tasks;
using Weasel.Postgresql;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using Weasel.Core;
using Xunit;

namespace Marten.Schema.Testing
{
    public class SystemFunctionTests : IntegrationContext
    {
        [Fact]
        public async Task generate_schema_objects_if_necessary()
        {
            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var cmd = new NpgsqlCommand("drop function if exists public.mt_immutable_timestamptz(text)");
                await conn.ExecuteAsync(cmd);
            }

            (await theStore.Tenancy.Default.DefinitionForFunction(new DbObjectName(SchemaConstants.DefaultSchema, "mt_immutable_timestamtzp")))
                .ShouldBeNull();

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();

            theStore.Tenancy.Default.EnsureStorageExists(typeof(SystemFunctions));

            (await theStore.Tenancy.Default.DefinitionForFunction(new DbObjectName(SchemaConstants.DefaultSchema, "mt_immutable_timestamptz")))
                .ShouldNotBeNull();
        }


    }
}
