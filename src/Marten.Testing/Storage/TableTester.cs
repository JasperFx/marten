using System;
using System.IO;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Storage
{
    public class TableTester : IDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly Table theTable;

        public TableTester()
        {
            _conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            _conn.Open();

            _conn.CreateCommand("drop schema if exists testbed cascade;create schema testbed")
                .ExecuteNonQuery();


            theTable = new Table(new DbObjectName("testbed", "table1"));
            theTable.AddPrimaryKey(new TableColumn("id", "uuid"));

            theTable.AddColumn("name", "text");
            theTable.AddColumn("number", "int");
        }

        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }

        private void writeTable()
        {
            var writer = new StringWriter();
            theTable.Write(new DdlRules(), writer);

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

        private bool tableExists()
        {
            var sql = "select count(*) from pg_stat_user_tables where relname = 'table1' and schemaname = 'testbed'";

            var count = _conn.CreateCommand(sql).ExecuteScalar().As<long>();
            return count == 1;
        }

        [Fact]
        public void can_write_the_initial_table()
        {
            writeTable();

            tableExists().ShouldBeTrue();

            _conn.CreateCommand("insert into testbed.table1 (id, name, number) values (:id, 'Jeremy', 42)")
                .With("id", Guid.NewGuid()).ExecuteNonQuery();

            _conn.CreateCommand("select count(*) from testbed.table1")
                .ExecuteScalar().As<long>().ShouldBe(1);
        }
    }
}