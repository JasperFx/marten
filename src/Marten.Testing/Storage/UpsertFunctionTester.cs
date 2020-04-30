using System;
using System.IO;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Storage
{
    public class UpsertFunctionTester : IDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly DocumentMapping<User> theMapping;

        public UpsertFunctionTester()
        {
            _conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            _conn.Open();

            _conn.CreateCommand("drop schema if exists testbed cascade;create schema testbed")
                .ExecuteNonQuery();

            theMapping = DocumentMapping.For<User>();
            theMapping.DatabaseSchemaName = "testbed";

            // write the initial table
            writeTable();
        }

        private void writeTable()
        {
            var table = new DocumentTable(theMapping);

            var patch = new SchemaPatch(new DdlRules());

            var cmd = _conn.CreateCommand();
            var builder = new CommandBuilder(cmd);

            table.ConfigureQueryCommand(builder);
            cmd.CommandText = builder.ToString();

            try
            {
                using (var reader = cmd.ExecuteReader())
                {
                    table.CreatePatch(reader, patch, AutoCreate.All);
                }

                _conn.CreateCommand(patch.UpdateDDL).ExecuteNonQuery();
            }
            catch (Exception)
            {
                Console.WriteLine(builder.ToString());
                throw;
            }
        }

        private void writeFunction()
        {
            var writer = new StringWriter();
            var function = new UpsertFunction(theMapping);
            function.Write(new DdlRules(), writer);

            var sql = writer.ToString();

            try
            {
                _conn.CreateCommand(sql).ExecuteNonQuery();
            }
            catch (Exception)
            {
                Console.WriteLine(sql);
                throw;
            }
        }

        private bool functionExists()
        {
            var function = new UpsertFunction(theMapping);
            var sql = $@"
select count(*)
from pg_proc JOIN pg_namespace as ns ON pg_proc.pronamespace = ns.oid
where ns.nspname = 'testbed' and pg_proc.proname = '{function.Identifier.Name}';";

            var count = _conn.CreateCommand(sql).ExecuteScalar().As<long>();
            return count == 1;
        }

        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }

        [Fact]
        public void can_create_the_initial_function()
        {
            functionExists().ShouldBeFalse();
            writeFunction();
            functionExists().ShouldBeTrue();
        }

        [Fact]
        public void detect_when_the_function_is_missing()
        {
            var function = new UpsertFunction(theMapping);

            var delta = function.FetchDelta(_conn, new DdlRules());

            SpecificationExtensions.ShouldBeNull(delta);
        }

        [Fact]
        public void detect_that_there_are_no_changes()
        {
            writeFunction();

            var function = new UpsertFunction(theMapping);

            var delta = function.FetchDelta(_conn, new DdlRules());
            delta.AllNew.ShouldBeFalse();
            delta.HasChanged.ShouldBeFalse();
        }

        [Fact]
        public void detect_that_the_function_has_changed()
        {
            writeFunction();

            theMapping.Duplicate(x => x.FirstName);
            writeTable();

            var function = new UpsertFunction(theMapping);

            var delta = function.FetchDelta(_conn, new DdlRules());

            delta.AllNew.ShouldBeFalse();
            delta.HasChanged.ShouldBeTrue();
        }

        [Fact]
        public void restore_previous_function_in_rollback()
        {
            writeFunction();

            theMapping.Duplicate(x => x.FirstName);
            writeTable();

            var function = new UpsertFunction(theMapping);

            var ddlRules = new DdlRules();
            var delta = function.FetchDelta(_conn, ddlRules);

            var patch = new SchemaPatch(ddlRules);
            delta.WritePatch(patch);

            SpecificationExtensions.ShouldContain(patch.RollbackDDL, delta.Actual.Body);
        }
    }
}
