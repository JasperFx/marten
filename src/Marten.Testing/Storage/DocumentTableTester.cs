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

        private void removeColumn(string name)
        {
            var sql = $"alter table {theMapping.Table} alter {name} DROP DEFAULT;";
            _conn.CreateCommand(sql).ExecuteNonQuery();
        }

        private void writeAndApplyPatch(AutoCreate autoCreate, DocumentTable table)
        {
            var patch = new SchemaPatch(new DdlRules());
            patch.Apply(new ConnectionSource(), autoCreate, new ISchemaObject[] {table});

            _conn.CreateCommand(patch.UpdateDDL).ExecuteNonQuery();
        }

        [Fact]
        public void can_migrate_missing_duplicated_fields()
        {
            writeTable();
            theMapping.Duplicate(x => x.FirstName);
            var newTable = new DocumentTable(theMapping);

            writeAndApplyPatch(AutoCreate.CreateOrUpdate, newTable);

            var theActual = theTable.FetchExisting(_conn);

            theActual.HasColumn("first_name");
        }

        [Fact]
        public void can_migrate_missing_soft_deleted_columns()
        {
            writeTable();
            theMapping.DeleteStyle = DeleteStyle.SoftDelete;
            var newTable = new DocumentTable(theMapping);

            writeAndApplyPatch(AutoCreate.CreateOrUpdate, newTable);

            var theActual = theTable.FetchExisting(_conn);

            theActual.HasColumn(DocumentMapping.DeletedColumn);
            theActual.HasColumn(DocumentMapping.DeletedAtColumn);
        }

        [Fact]
        public void can_migrate_missing_hierarchical_columns()
        {
            writeTable();
            theMapping.AddSubClass(typeof(SuperUser));
            var newTable = new DocumentTable(theMapping);

            writeAndApplyPatch(AutoCreate.CreateOrUpdate, newTable);

            var theActual = theTable.FetchExisting(_conn);

            theActual.HasColumn(DocumentMapping.DocumentTypeColumn);
        }

        [Theory]
        [InlineData(DocumentMapping.DotNetTypeColumn)]
        [InlineData(DocumentMapping.LastModifiedColumn)]
        [InlineData(DocumentMapping.VersionColumn)]
        public void can_migrate_missing_metadata_column(string columnName)
        {
            writeTable();
            removeColumn(columnName);

            writeAndApplyPatch(AutoCreate.CreateOrUpdate, theTable);

            var theActual = theTable.FetchExisting(_conn);

            theActual.HasColumn(columnName);
        }
    }
}