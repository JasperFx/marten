using Baseline;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Generation
{
    public class TableDefinitionTests
    {
        [Fact]
        public void equivalency_positive()
        {
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.StorageTable();
            var table2 = users.StorageTable();

            table2.ShouldBe(table1);
            table1.ShouldBe(table2);
            table1.ShouldNotBeSameAs(table2);
        }

        [Fact]
        public void equivalency_negative_different_numbers_of_columns()
        {
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.StorageTable();
            var table2 = users.StorageTable();

            table2.Columns.Add(new TableColumn("user_name", "character varying"));

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void equivalency_negative_column_type_changed()
        {
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.StorageTable();
            var table2 = users.StorageTable();

            table2.ReplaceOrAddColumn(table2.PrimaryKey.Name, "int", table2.PrimaryKey.Directive);

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void equivalency_positive_column_name_case_insensitive()
        {
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.StorageTable();
            var table2 = users.StorageTable();

            table2.Column("username").ShouldBeSameAs(table1.Column("UserName"));

            table2.ShouldBe(table1);
        }

        [Fact]
        public void equivalency_with_the_postgres_synonym_issue()
        {
            // This was meant to address GH-127

            var users = DocumentMapping.For<User>();
            users.DuplicateField("FirstName");

            var table1 = users.SchemaObjects.As<DocumentSchemaObjects>().StorageTable();
            var table2 = users.SchemaObjects.As<DocumentSchemaObjects>().StorageTable();

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
        public void write_ddl_in_default_drop_then_create_mode()
        {
            var users = DocumentMapping.For<User>();
            var table = users.SchemaObjects.StorageTable();
            var rules = new DdlRules
            {
                TableCreation = CreationStyle.DropThenCreate
            };

            var ddl = table.ToDDL(rules);

            ddl.ShouldContain("DROP TABLE IF EXISTS public.mt_doc_user CASCADE;");
            ddl.ShouldContain("CREATE TABLE public.mt_doc_user");

        }

        [Fact]
        public void write_ddl_in_create_if_not_exists_mode()
        {
            var users = DocumentMapping.For<User>();
            var table = users.SchemaObjects.StorageTable();
            var rules = new DdlRules
            {
                TableCreation = CreationStyle.CreateIfNotExists
            };

            var ddl = table.ToDDL(rules);

            ddl.ShouldNotContain("DROP TABLE IF EXISTS public.mt_doc_user CASCADE;");
            ddl.ShouldContain("CREATE TABLE IF NOT EXISTS public.mt_doc_user");
        }

        [Fact]
        public void can_do_substitutions()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Duplicate(x => x.FirstName);


            var users = new DocumentSchemaObjects(mapping);
            
            var table = users.StorageTable();

            table.BuildTemplate($"*{DdlRules.SCHEMA}*").ShouldBe($"*{table.Name.Schema}*");
            table.BuildTemplate($"*{DdlRules.TABLENAME}*").ShouldBe($"*{table.Name.Name}*");
            table.BuildTemplate($"*{DdlRules.COLUMNS}*").ShouldBe($"*id, data, mt_last_modified, mt_version, mt_dotnet_type, first_name*");
            table.BuildTemplate($"*{DdlRules.NON_ID_COLUMNS}*").ShouldBe($"*data, mt_last_modified, mt_version, mt_dotnet_type, first_name*");

            table.BuildTemplate($"*{DdlRules.METADATA_COLUMNS}*").ShouldBe("*mt_last_modified, mt_version, mt_dotnet_type*");
        }
    }

    public class using_custom_ddl_rules_smoke_tests : IntegratedFixture
    {
        [Fact]
        public void can_use_CreateIfNotExists()
        {
            StoreOptions(_ =>
            {
                _.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;
            });

            // Would blow up if it doesn't work;)
            theStore.Schema.EnsureStorageExists(typeof(User));
        }

        
    }
}