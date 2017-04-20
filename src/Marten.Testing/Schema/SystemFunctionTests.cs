using System;
using Marten.Schema;
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

            theStore.Schema.EnsureFunctionExists("mt_immutable_timestamp");

            theStore.Schema.DbObjects.DefinitionForFunction(new DbObjectName("public", "mt_immutable_timestamp"))
                .ShouldNotBeNull();
        }

        [Fact]
        public void patch_with_auto_create_to_none_throws_exception()
        {
            StoreOptions(_ => _.AutoCreateSchemaObjects = AutoCreate.None);

            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                theStore.Schema.EnsureFunctionExists("mt_immutable_timestamp");
            });
        }

    }
}