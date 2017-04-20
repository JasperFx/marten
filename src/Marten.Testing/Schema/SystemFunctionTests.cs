using System;
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
            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(
                    cmd => cmd.Sql("drop function if exists public.mt_immutable_timestamp(text)").ExecuteNonQuery());
            }

            theStore.Schema.DbObjects.DefinitionForFunction(new DbObjectName("public", "mt_immutable_timestamp"))
                .ShouldBeNull();

            theStore.DefaultTenant.ResetSchemaExistenceChecks();

            theStore.DefaultTenant.EnsureStorageExists(typeof(SystemFunctions));

            theStore.Schema.DbObjects.DefinitionForFunction(new DbObjectName("public", "mt_immutable_timestamp"))
                .ShouldNotBeNull();
        }


    }
}