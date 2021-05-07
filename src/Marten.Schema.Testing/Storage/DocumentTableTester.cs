using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Exceptions;
using Marten.Internal.CodeGeneration;
using Weasel.Postgresql;
using Marten.Schema.Testing.Documents;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql.Tables;
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
            table.WriteCreateStatement(new DdlRules(), writer);

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
            var sql = $"alter table {theMapping.TableName} alter {name} DROP DEFAULT;";
            _conn.CreateCommand(sql).ExecuteNonQuery();
        }

        private async Task writeAndApplyPatch(AutoCreate autoCreate, DocumentTable table)
        {
            var migration = await SchemaMigration.Determine(_conn, new ISchemaObject[] {table});


            if (migration.Difference != SchemaPatchDifference.None)
            {
                await migration.ApplyAll(_conn, new DdlRules(), autoCreate);
            }

        }

        [Theory]
        [InlineData(SchemaConstants.DotNetTypeColumn)]
        [InlineData(SchemaConstants.LastModifiedColumn)]
        [InlineData(SchemaConstants.VersionColumn)]
        public async Task can_migrate_missing_metadata_column(string columnName)
        {
            writeTable();
            removeColumn(columnName);

            await writeAndApplyPatch(AutoCreate.CreateOrUpdate, theTable);

            var theActual = await theTable.FetchExisting(_conn);

            theActual.HasColumn(columnName);
        }

        [Fact]
        public void basic_columns()
        {
            theTable.Columns.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(
                    "id",
                    "data",
                    SchemaConstants.LastModifiedColumn,
                    SchemaConstants.VersionColumn,
                    SchemaConstants.DotNetTypeColumn);
        }

        [Fact]
        public async Task can_create_with_indexes()
        {
            theMapping.Index(x => x.UserName);
            theMapping.Index(x => x.FirstName);

            writeTable();

            var existing = await theTable.FetchExisting(_conn);
            existing.Indexes.Count.ShouldBe(2);
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
        public async Task can_migrate_missing_duplicated_fields()
        {
            writeTable();
            theMapping.Duplicate(x => x.FirstName);
            var newTable = new DocumentTable(theMapping);

            await writeAndApplyPatch(AutoCreate.CreateOrUpdate, newTable);

            var theActual = await theTable.FetchExisting(_conn);

            theActual.HasColumn("first_name");
        }

        [Fact]
        public async Task can_migrate_missing_hierarchical_columns()
        {
            writeTable();
            theMapping.SubClasses.Add(typeof(SuperUser));
            var newTable = new DocumentTable(theMapping);

            await writeAndApplyPatch(AutoCreate.CreateOrUpdate, newTable);

            var theActual = await theTable.FetchExisting(_conn);

            theActual.HasColumn(SchemaConstants.DocumentTypeColumn);
        }

        [Fact]
        public async Task can_migrate_missing_soft_deleted_columns()
        {
            writeTable();
            theMapping.DeleteStyle = DeleteStyle.SoftDelete;
            var newTable = new DocumentTable(theMapping);

            await writeAndApplyPatch(AutoCreate.CreateOrUpdate, newTable);

            var theActual = await theTable.FetchExisting(_conn);

            theActual.HasColumn(SchemaConstants.DeletedColumn);
            theActual.HasColumn(SchemaConstants.DeletedAtColumn);
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

            var delta = new TableDelta(table1, table2);
            delta.Difference.ShouldBe(SchemaPatchDifference.None);

        }




        [Fact]
        public void hierarchical()
        {
            theMapping.SubClasses.Add(typeof(SuperUser));

            theTable.HasColumn(SchemaConstants.DocumentTypeColumn);
        }


        [Fact]
        public void soft_deleted()
        {
            theMapping.DeleteStyle = DeleteStyle.SoftDelete;

            theTable.HasColumn(SchemaConstants.DeletedColumn);
            Assert.Contains(theTable.Indexes, x => x.Name == $"mt_doc_user_idx_{SchemaConstants.DeletedColumn}");
            theTable.HasColumn(SchemaConstants.DeletedAtColumn);
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

            var ddl = table.ToCreateSql(rules);

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

            var ddl = table.ToCreateSql(rules);

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
        [InlineData(StorageStyle.Lightweight, new string[]{"id", "data"})]
        [InlineData(StorageStyle.IdentityMap, new string[]{"id", "data"})]
        [InlineData(StorageStyle.DirtyTracking, new string[]{"id", "data"})]
        public void basic_select_columns(StorageStyle style, string[] expected)
        {
            theTable.SelectColumns(style)
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Theory]
        [InlineData(StorageStyle.QueryOnly, new string[]{"data"})]
        [InlineData(StorageStyle.Lightweight, new string[]{"id", "data", "mt_version"})]
        [InlineData(StorageStyle.IdentityMap, new string[]{"id", "data", "mt_version"})]
        [InlineData(StorageStyle.DirtyTracking, new string[]{"id", "data", "mt_version"})]
        public void basic_select_columns_with_optimistic_versioning(StorageStyle style, string[] expected)
        {
            theMapping.UseOptimisticConcurrency = true;

            theTable.SelectColumns(style)
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(expected);
        }


        [Theory]
        [InlineData(StorageStyle.QueryOnly, new string[]{"data", "mt_doc_type"})]
        [InlineData(StorageStyle.Lightweight, new string[]{"id", "data", "mt_doc_type"})]
        [InlineData(StorageStyle.IdentityMap, new string[]{"id", "data", "mt_doc_type"})]
        [InlineData(StorageStyle.DirtyTracking, new string[]{"id", "data", "mt_doc_type"})]
        public void select_columns_with_hierarchy(StorageStyle style, string[] expected)
        {
            theMapping.SubClasses.AddHierarchy();

            theTable.SelectColumns(style)
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Theory]
        [InlineData(StorageStyle.QueryOnly, new string[]{"data", "mt_doc_type"})]
        [InlineData(StorageStyle.Lightweight, new string[]{"id", "data", "mt_doc_type", "mt_version"})]
        [InlineData(StorageStyle.IdentityMap, new string[]{"id", "data", "mt_doc_type", "mt_version"})]
        [InlineData(StorageStyle.DirtyTracking, new string[]{"id", "data", "mt_doc_type", "mt_version"})]
        public void select_columns_with_hierarchy_with_optimistic_versioning(StorageStyle style, string[] expected)
        {
            theMapping.SubClasses.AddHierarchy();
            theMapping.UseOptimisticConcurrency = true;

            theTable.SelectColumns(style)
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs(expected);
        }



    }
}
