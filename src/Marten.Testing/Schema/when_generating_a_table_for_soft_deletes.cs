using System.Linq;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class when_generating_a_table_for_soft_deletes
    {
        private TableDefinition theTable;

        public when_generating_a_table_for_soft_deletes()
        {
            var mapping = DocumentMapping.For<Target>();
            mapping.DeleteStyle = DeleteStyle.SoftDelete;

            var schemaObjects = new DocumentSchemaObjects(mapping);

            theTable = schemaObjects.StorageTable();
        }

        [Fact]
        public void has_a_column_for_the_deleted_mark()
        {
            var column = theTable.Column(DocumentMapping.DeletedColumn);
            column.Directive.ShouldBe("DEFAULT FALSE");
            column.Type.ShouldBe("boolean");
        }

        [Fact]
        public void has_a_column_for_the_deleted_at_mark()
        {
            var column = theTable.Column(DocumentMapping.DeletedAtColumn);
            column.Directive.ShouldBe("NULL");
            column.Type.ShouldBe("timestamp with time zone");
        }

        [Fact]
        public void can_generate_the_patch()
        {
            using (var store1 = TestingDocumentStore.Basic())
            {
                store1.BulkInsert(new User [] {new User {UserName = "foo"}, new User { UserName = "bar" } });
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().SoftDeleted();
            }))
            {
                // Verifying that we didn't lose any data
                using (var session = store2.QuerySession())
                {
                    session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                        .ToList().ShouldHaveTheSameElementsAs("bar", "foo");
                }

                var table = store2.Schema.DbObjects.TableSchema(store2.Schema.MappingFor(typeof(User)));

                table.HasColumn(DocumentMapping.DeletedColumn).ShouldBeTrue();
                table.HasColumn(DocumentMapping.DeletedAtColumn).ShouldBeTrue();
            }
        }
    }
}