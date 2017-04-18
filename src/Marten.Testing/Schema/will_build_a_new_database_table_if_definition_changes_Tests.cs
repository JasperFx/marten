using System;
using System.Linq;
using Baseline;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema
{
    public class will_build_a_new_database_table_if_definition_changes_Tests
    {
        [Fact]
        public void will_build_the_new_table_if_the_configured_table_does_not_match_the_existing_table()
        {
            TableDefinition table1;
            TableDefinition table2;

            using (var container = ContainerFactory.Default())
            {
                var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();

                store.Advanced.Clean.CompletelyRemoveAll();

                store.DefaultTenant.StorageFor(typeof (User));

                store.Schema.DbObjects.DocumentTables().ShouldContain("public.mt_doc_user");

                table1 = store.TableSchema(typeof(User));
                table1.Columns.ShouldNotContain(x => x.Name == "user_name");
            }

            using (var container = ContainerFactory.Default())
            {
                var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();

                store.Storage.MappingFor(typeof (User)).As<DocumentMapping>().DuplicateField("UserName");

                store.DefaultTenant.StorageFor(typeof(User));

                store.Schema.DbObjects.DocumentTables().ShouldContain("public.mt_doc_user");

                table2 = store.TableSchema(typeof (User));
            }

            table2.ShouldNotBe(table1);

            table2.Column("user_name").ShouldNotBeNull();
        }

        [Fact]
        public void will_build_the_new_table_if_the_configured_table_does_not_match_the_existing_table_on_other_schema()
        {
            TableDefinition table1;
            TableDefinition table2;

            using (var container = ContainerFactory.OnOtherDatabaseSchema())
            {
                var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();

                store.Advanced.Clean.CompletelyRemoveAll();

                store.DefaultTenant.EnsureStorageExists(typeof(User));

                store.Schema.DbObjects.DocumentTables().ShouldContain("other.mt_doc_user");

                table1 = store.TableSchema(typeof(User));
                table1.Columns.ShouldNotContain(x => x.Name == "user_name");
            }

            using (var container = ContainerFactory.OnOtherDatabaseSchema())
            {
                var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();

                store.Storage.MappingFor(typeof(User)).As<DocumentMapping>().DuplicateField("UserName");

                store.DefaultTenant.EnsureStorageExists(typeof(User));

                store.Schema.DbObjects.DocumentTables().ShouldContain("other.mt_doc_user");

                table2 = store.TableSchema(typeof(User));
            }

            table2.ShouldNotBe(table1);

            table2.Column("user_name").ShouldNotBeNull();
        }
    }
}