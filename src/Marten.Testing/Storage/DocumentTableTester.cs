using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Util;
using Npgsql;
using Xunit;

namespace Marten.Testing.Storage
{
    public class DocumentTableTester : IDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly Lazy<DocumentTable> _table;
        private DocumentMapping<User> theMapping;

        public DocumentTableTester()
        {
            _conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            _conn.Open();

            _conn.CreateCommand("drop schema if exists testbed cascade;create schema testbed")
                .ExecuteNonQuery();

            theMapping = DocumentMapping.For<User>();
            theMapping.DatabaseSchemaName = "testbed";

            _table = new Lazy<DocumentTable>(() => new DocumentTable(theMapping));
        }

        protected DocumentTable theTable => _table.Value;

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
            var sql = $"select count(*) from pg_stat_user_tables where relname = '{theTable.Identifier.Name}' and schemaname = 'testbed'";

            var count = _conn.CreateCommand(sql).ExecuteScalar().As<long>();
            return count == 1;
        }

        public void Dispose()
        {
            _conn.Dispose();
        }

        [Fact]
        public void basic_columns()
        {
            theTable.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(
                    "id", 
                    "data", 
                    DocumentMapping.LastModifiedColumn, 
                    DocumentMapping.VersionColumn, 
                    DocumentMapping.DotNetTypeColumn);
        }

        [Fact]
        public void can_write_the_basic_table()
        {
            tableExists().ShouldBeFalse();

            writeTable();

            tableExists().ShouldBeTrue();
        }

        [Fact]
        public void duplicated_fields()
        {
            theMapping.Duplicate(x => x.FirstName);
            theMapping.Duplicate(x => x.LastName);

            theTable.HasColumn("first_name").ShouldBeTrue();
            theTable.HasColumn("last_name").ShouldBeTrue();
        }

        [Fact]
        public void soft_deleted()
        {
            theMapping.DeleteStyle = DeleteStyle.SoftDelete;

            theTable.HasColumn(DocumentMapping.DeletedColumn);
            theTable.HasColumn(DocumentMapping.DeletedAtColumn);
        }

        [Fact]
        public void hierarchical()
        {
            theMapping.AddSubClass(typeof(SuperUser));

            theTable.HasColumn(DocumentMapping.DocumentTypeColumn);
        }


    }
}