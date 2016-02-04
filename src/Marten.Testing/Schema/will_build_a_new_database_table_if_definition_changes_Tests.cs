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

            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();

                store.Advanced.Clean.CompletelyRemoveAll();

                store.Schema.StorageFor(typeof (User));

                store.Schema.DocumentTables().ShouldContain(x => x == "mt_doc_user");

                table1 = store.Schema.TableSchema("mt_doc_user");
                table1.Columns.Any(x => x.Name == "user_name").ShouldBeFalse();
            }

            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();

                store.Schema.MappingFor(typeof (User)).As<DocumentMapping>().DuplicateField("UserName");

                store.Schema.StorageFor(typeof(User));

                store.Schema.DocumentTables().ShouldContain(x => x == "mt_doc_user");

                table2 = store.Schema.TableSchema("mt_doc_user");
            }

            table2.ShouldNotBe(table1);

            table2.Column("user_name").ShouldNotBeNull();
        }
    }
}