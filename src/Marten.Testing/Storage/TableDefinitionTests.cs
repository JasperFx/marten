using System;
using Baseline;
using Marten.Generation;
using Marten.Schema;
using Marten.Storage;
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
            var users = DocumentMapping.For<User>();
            var table1 = new DocumentTable(users);
            var table2 = new DocumentTable(users);

            table2.ShouldBe(table1);
            table1.ShouldBe(table2);
            table1.ShouldNotBeSameAs(table2);
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
        public void equivalency_negative_column_type_changed()
        {
            var users = DocumentMapping.For<User>();
            var table1 = new DocumentTable(users);
            var table2 = new DocumentTable(users);

            table2.ReplaceOrAddColumn(table2.PrimaryKey.Name, "int", table2.PrimaryKey.Directive);

            table2.ShouldNotBe(table1);
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
        public void can_do_substitutions()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Duplicate(x => x.FirstName);
            
            var table = new DocumentTable(mapping);
            throw new NotImplementedException("Need to redo the silly BuildTemplate");
//            table.BuildTemplate($"*{DdlRules.SCHEMA}*").ShouldBe($"*{table.Identifier.Schema}*");
//            table.BuildTemplate($"*{DdlRules.TABLENAME}*").ShouldBe($"*{table.Identifier.Name}*");
//            table.BuildTemplate($"*{DdlRules.COLUMNS}*").ShouldBe($"*id, data, mt_last_modified, mt_version, mt_dotnet_type, first_name*");
//            table.BuildTemplate($"*{DdlRules.NON_ID_COLUMNS}*").ShouldBe($"*data, mt_last_modified, mt_version, mt_dotnet_type, first_name*");
//
//            table.BuildTemplate($"*{DdlRules.METADATA_COLUMNS}*").ShouldBe("*mt_last_modified, mt_version, mt_dotnet_type*");
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
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));
        }

        
    }
}