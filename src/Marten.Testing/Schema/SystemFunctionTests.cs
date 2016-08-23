using System;
using System.IO;
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

            theStore.Schema.DbObjects.DefinitionForFunction(new FunctionName("public", "mt_immutable_timestamp"))
                .ShouldBeNull();

            theStore.Schema.ResetSchemaExistenceChecks();

            theStore.Schema.EnsureFunctionExists("mt_immutable_timestamp");

            theStore.Schema.DbObjects.DefinitionForFunction(new FunctionName("public", "mt_immutable_timestamp"))
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

        [Fact]
        public void can_write_schema_objects()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(
                    cmd => cmd.Sql("drop function if exists other.mt_immutable_timestamp(text)").ExecuteNonQuery());
            }

            var function = new SystemFunction(theStore.Advanced.Options, "mt_immutable_timestamp", "text");

            var writer = new StringWriter();

            function.WriteSchemaObjects(theStore.Schema, writer);

            writer.ToString().ShouldContain("CREATE OR REPLACE FUNCTION other.mt_immutable_timestamp(value text)");
        }

        [Fact]
        public void can_write_schema_objects_with_grants()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.DdlRules.Grants.Add("foo");
            });

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(
                    cmd => cmd.Sql("drop function if exists other.mt_immutable_timestamp(text)").ExecuteNonQuery());
            }

            var function = new SystemFunction(theStore.Advanced.Options, "mt_immutable_timestamp", "text");

            var writer = new StringWriter();

            function.WriteSchemaObjects(theStore.Schema, writer);

            writer.ToString().ShouldContain("GRANT EXECUTE ON other.mt_immutable_timestamp(text) TO \"foo\";");
        }

        [Fact]
        public void can_patch_if_missing()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(
                    cmd => cmd.Sql("drop function if exists other.mt_immutable_timestamp(text)").ExecuteNonQuery());
            }

                var function = new SystemFunction(theStore.Advanced.Options, "mt_immutable_timestamp", "text");

            var patch = new SchemaPatch(new DdlRules());

            function.WritePatch(theStore.Schema, patch);

            patch.UpdateDDL.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_immutable_timestamp(value text)");
            patch.RollbackDDL.ShouldContain("drop function if exists other.mt_immutable_timestamp");
        }
    }
}