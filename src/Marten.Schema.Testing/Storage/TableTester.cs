using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Storage;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    [Collection("testbed")]
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
            theTable.AddColumn("rownum", "serial");
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

        [Fact]
        public void can_recognize_when_the_existing_table_does_not_exist()
        {
            tableExists().ShouldBeFalse();

            SpecificationExtensions.ShouldBeNull(theTable.FetchExisting(_conn));
        }

        [Fact]
        public void can_fetch_the_existing_table()
        {
            writeTable();
            tableExists().ShouldBeTrue();

            var existing = theTable.FetchExisting(_conn);

            SpecificationExtensions.ShouldNotBeNull(existing);

            existing.PrimaryKey.Name.ShouldBe("id");
            existing.Select(x => x.Name).ShouldHaveTheSameElementsAs("id", "name", "number", "rownum");
        }



        [Fact]
        public void perfect_match()
        {
            writeTable();

            var diff = theTable.FetchDelta(_conn);
            SpecificationExtensions.ShouldNotBeNull(diff);
            diff.Matched.Select(x => x.Name).ShouldHaveTheSameElementsAs("id", "name", "number", "rownum");
            diff.Missing.Length.ShouldBe(0);
            diff.Extras.Length.ShouldBe(0);
            diff.Different.Length.ShouldBe(0);

            diff.Matches.ShouldBeTrue();
        }

        [Fact]
        public void not_matching_with_missing_columns()
        {
            writeTable();

            theTable.AddColumn("newcol", "text");

            var diff = theTable.FetchDelta(_conn);
            diff.Matches.ShouldBeFalse();

            diff.Missing.Single().Name.ShouldBe("newcol");
            diff.Extras.Any().ShouldBeFalse();
            diff.Different.Any().ShouldBeFalse();
        }

        [Fact]
        public void not_matching_with_extra_columns()
        {
            var tableColumn = new TableColumn("new", "varchar");
            theTable.AddColumn(tableColumn);

            writeTable();

            theTable.RemoveColumn("new");


            var diff = theTable.FetchDelta(_conn);

            diff.Matches.ShouldBeFalse();
            diff.Extras.Single().ShouldBe(tableColumn);
        }

        [Fact]
        public void not_matching_with_columns_of_same_name_that_are_different()
        {
            writeTable();
            theTable.ColumnFor("id").Type = "int";

            var diff = theTable.FetchDelta(_conn);
            diff.Matches.ShouldBeFalse();

            diff.Different.Single().Name.ShouldBe("id");
        }

        [Fact]
        public void matching_with_columns_same_name_and_synonymn_of_type()
        {
            //e.g. serial is an int
            writeTable();

            var existing = theTable.FetchExisting(_conn);
            existing._columns.First(c => c.Name == "rownum").Type.ShouldBe("integer");

            var diff = theTable.FetchDelta(_conn);
            diff.Matches.ShouldBeTrue();
        }




    }
}
