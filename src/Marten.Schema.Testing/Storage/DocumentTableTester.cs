using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Exceptions;
using Marten.Internal.CodeGeneration;
using Marten.Schema.Testing.Documents;
using Marten.Storage;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    [Collection("testbed")]
    public class DocumentTableTester : IDisposable
    {
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

        public void Dispose()
        {
            _conn.Dispose();
        }

        private readonly NpgsqlConnection _conn;
        private readonly Lazy<DocumentTable> _table;
        private readonly DocumentMapping<User> theMapping;

        internal DocumentTable theTable => _table.Value;

        private void writeTable(Table table = null)
        {
            table = table ?? theTable;

            var writer = new StringWriter();
            table.Write(new DdlRules(), writer);

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
            var sql =
                $"select count(*) from pg_stat_user_tables where relname = '{theTable.Identifier.Name}' and schemaname = 'testbed'";

            var count = _conn.CreateCommand(sql).ExecuteScalar().As<long>();
            return count == 1;
        }

        private void removeColumn(string name)
        {
            var sql = $"alter table {theMapping.Table} alter {name} DROP DEFAULT;";
            _conn.CreateCommand(sql).ExecuteNonQuery();
        }

        private void writeAndApplyPatch(AutoCreate autoCreate, DocumentTable table)
        {
            var patch = new SchemaPatch(new DdlRules());

            patch.Apply(_conn, autoCreate, new ISchemaObject[] {table});

            _conn.CreateCommand(patch.UpdateDDL).ExecuteNonQuery();
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
        public void can_create_with_indexes()
        {
            theMapping.Index(x => x.UserName);
            theMapping.Index(x => x.FirstName);

            writeTable();

            var existing = theTable.FetchExisting(_conn);
            existing.ActualIndices.Count.ShouldBe(2);
        }

        [Fact]
        public void can_do_substitutions()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Duplicate(x => x.FirstName);

            var table = new DocumentTable(mapping);
            table.BuildTemplate($"*{DdlRules.SCHEMA}*").ShouldBe($"*{table.Identifier.Schema}*");
            table.BuildTemplate($"*{DdlRules.TABLENAME}*").ShouldBe($"*{table.Identifier.Name}*");
            table.BuildTemplate($"*{DdlRules.COLUMNS}*")
                .ShouldBe($"*id, data, mt_last_modified, mt_version, mt_dotnet_type, first_name*");
            table.BuildTemplate($"*{DdlRules.NON_ID_COLUMNS}*")
                .ShouldBe($"*data, mt_last_modified, mt_version, mt_dotnet_type, first_name*");

            table.BuildTemplate($"*{DdlRules.METADATA_COLUMNS}*")
                .ShouldBe("*mt_last_modified, mt_version, mt_dotnet_type*");
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
        public void can_migrate_missing_hierarchical_columns()
        {
            writeTable();
            theMapping.AddSubClass(typeof(SuperUser));
            var newTable = new DocumentTable(theMapping);

            writeAndApplyPatch(AutoCreate.CreateOrUpdate, newTable);

            var theActual = theTable.FetchExisting(_conn);

            theActual.HasColumn(DocumentMapping.DocumentTypeColumn);
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
        public void can_spot_an_extra_index()
        {
            theMapping.Index(x => x.UserName);

            writeTable();

            theMapping.Index(x => x.FirstName);
            var table = new DocumentTable(theMapping);

            var delta = table.FetchDelta(_conn);

            delta.IndexChanges.Count.ShouldBe(1);
            delta.IndexRollbacks.Count.ShouldBe(1);
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
        public void equivalency_negative_column_type_changed()
        {
            var users = DocumentMapping.For<User>();
            var table1 = new DocumentTable(users);
            var table2 = new DocumentTable(users);

            table2.ReplaceOrAddColumn(table2.PrimaryKey.Name, "int", table2.PrimaryKey.Directive);

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void equivalency_negative_different_numbers_of_columns()
        {
            var users = DocumentMapping.For<User>();
            var table1 = new DocumentTable(users);
            var table2 = new DocumentTable(users);

            table2.AddColumn(new TableColumn("user_name", "character varying"));

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void equivalency_positive()
        {
            var users = DocumentMapping.For<User>();
            var table1 = new DocumentTable(users);
            var table2 = new DocumentTable(users);

            table2.ShouldBe(table1);
            table1.ShouldBe(table2);
            table1.ShouldNotBeSameAs(table2);
        }


        [Fact]
        public void equivalency_with_the_postgres_synonym_issue()
        {
            // This was meant to address GH-127

            var users = DocumentMapping.For<User>();
            users.DuplicateField("FirstName");

            var table1 = new DocumentTable(users);
            var table2 = new DocumentTable(users);

            table1.ReplaceOrAddColumn("first_name", "varchar");
            table2.ReplaceOrAddColumn("first_name", "character varying");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.ReplaceOrAddColumn("first_name", "character varying");
            table2.ReplaceOrAddColumn("first_name", "varchar");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.ReplaceOrAddColumn("first_name", "character varying");
            table2.ReplaceOrAddColumn("first_name", "character varying");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.ReplaceOrAddColumn("first_name", "varchar");
            table2.ReplaceOrAddColumn("first_name", "varchar");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();
        }

        [Fact]
        public void hierarchical()
        {
            theMapping.AddSubClass(typeof(SuperUser));

            theTable.HasColumn(DocumentMapping.DocumentTypeColumn);
        }

        [Fact]
        public void matches_on_indexes()
        {
            theMapping.Index(x => x.UserName);
            theMapping.Index(x => x.FirstName);

            writeTable();

            var delta = theTable.FetchDelta(_conn);

            delta.IndexChanges.Any().ShouldBeFalse();
            delta.IndexRollbacks.Any().ShouldBeFalse();
        }

        [Fact]
        public void soft_deleted()
        {
            theMapping.DeleteStyle = DeleteStyle.SoftDelete;

            theTable.HasColumn(DocumentMapping.DeletedColumn);
            Assert.Contains(theTable.Indexes, x => x.IndexName == $"mt_doc_user_idx_{DocumentMapping.DeletedColumn}");
            theTable.HasColumn(DocumentMapping.DeletedAtColumn);
        }

        [Fact]
        public void write_ddl_in_create_if_not_exists_mode()
        {
            var users = DocumentMapping.For<User>();
            var table = new DocumentTable(users);
            var rules = new DdlRules
            {
                TableCreation = CreationStyle.CreateIfNotExists
            };

            var ddl = table.ToDDL(rules);

            ddl.ShouldNotContain("DROP TABLE IF EXISTS public.mt_doc_user CASCADE;");
            ddl.ShouldContain("CREATE TABLE IF NOT EXISTS public.mt_doc_user");
        }

        [Fact]
        public void write_ddl_in_default_drop_then_create_mode()
        {
            var users = DocumentMapping.For<User>();
            var table = new DocumentTable(users);
            var rules = new DdlRules
            {
                TableCreation = CreationStyle.DropThenCreate
            };

            var ddl = table.ToDDL(rules);

            ddl.ShouldContain("DROP TABLE IF EXISTS public.mt_doc_user CASCADE;");
            ddl.ShouldContain("CREATE TABLE public.mt_doc_user");
        }

        [Fact]
        public void invalid_document_should_throw_exception()
        {
            var docs = DocumentMapping.For<InvalidDocument>();

            var ex =
                Exception<InvalidDocumentException>.ShouldBeThrownBy(
                    () => new DocumentTable(docs));
            ex.Message.ShouldContain($"Could not determine an 'id/Id' field or property for requested document type {typeof(InvalidDocument).FullName}");
        }

        [Theory]
        [InlineData(StorageStyle.QueryOnly, new string[]{"data"})]
        [InlineData(StorageStyle.Lightweight, new string[]{"id", "data", "mt_version"})]
        [InlineData(StorageStyle.IdentityMap, new string[]{"id", "data", "mt_version"})]
        [InlineData(StorageStyle.DirtyTracking, new string[]{"id", "data", "mt_version"})]
        public void basic_select_columns(StorageStyle style, string[] expected)
        {
            theTable.SelectColumns(style)
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Theory]
        [InlineData(StorageStyle.QueryOnly, new string[]{"data", "mt_doc_type"})]
        [InlineData(StorageStyle.Lightweight, new string[]{"id", "data", "mt_doc_type", "mt_version"})]
        [InlineData(StorageStyle.IdentityMap, new string[]{"id", "data", "mt_doc_type", "mt_version"})]
        [InlineData(StorageStyle.DirtyTracking, new string[]{"id", "data", "mt_doc_type", "mt_version"})]
        public void select_columns_with_hierarchy(StorageStyle style, string[] expected)
        {
            theMapping.AddSubClassHierarchy();

            theTable.SelectColumns(style)
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(expected);
        }


    }
}
