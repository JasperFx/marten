using System.Linq;
using Marten.Schema.Testing.Documents;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    [Collection("soft_deletes")]
    public class when_generating_a_table_for_soft_deletes : IntegrationContext
    {
        private DocumentTable theTable;

        public when_generating_a_table_for_soft_deletes()
        {

            var mapping = DocumentMapping.For<Target>();
            mapping.DeleteStyle = DeleteStyle.SoftDelete;


            theTable = new DocumentTable(mapping);
        }

        [Fact]
        public void has_a_column_for_the_deleted_mark()
        {
            var column = theTable.Column(SchemaConstants.DeletedColumn);
            column.Directive.ShouldBe("DEFAULT FALSE");
            column.Type.ShouldBe("boolean");
        }

        [Fact]
        public void has_a_column_for_the_deleted_at_mark()
        {
            var column = theTable.Column(SchemaConstants.DeletedAtColumn);
            column.Directive.ShouldBe("NULL");
            column.Type.ShouldBe("timestamp with time zone");
        }

        [Fact]
        public void can_generate_the_patch()
        {
            using (var store1 = SeparateStore(x=> x.AutoCreateSchemaObjects = AutoCreate.All))
            {

                store1.BulkInsert(new User [] {new User {UserName = "foo"}, new User { UserName = "bar" } });
            }

            using (var store2 = SeparateStore(_ =>
            {
                _.Schema.For<User>().SoftDeleted();
            }))
            {
                // Verifying that we didn't lose any data
                using (var session = store2.QuerySession())
                {
                    session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                        .ToList().ShouldHaveTheSameElementsAs("bar", "foo");
                }


                var table = store2.Tenancy.Default.DbObjects.ExistingTableFor(typeof(User));

                table.HasColumn(SchemaConstants.DeletedColumn).ShouldBeTrue();
                table.HasColumn(SchemaConstants.DeletedAtColumn).ShouldBeTrue();
            }
        }

        [Fact]
        public void can_generate_the_patch_with_camel_casing()
        {
            using (var store1 = StoreOptions(_ =>
            {
                _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);
            }))
            {
                store1.BulkInsert(new User[] { new User { UserName = "foo" }, new User { UserName = "bar" } });
            }

            using (var store2 = SeparateStore(_ =>
            {
                _.Schema.For<User>().SoftDeleted();
                _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);
            }))
            {
                // Verifying that we didn't lose any data
                using (var session = store2.QuerySession())
                {
                    session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                        .ToList().ShouldHaveTheSameElementsAs("bar", "foo");
                }


                var table = store2.Tenancy.Default.DbObjects.ExistingTableFor(typeof(User));

                table.HasColumn(SchemaConstants.DeletedColumn).ShouldBeTrue();
                table.HasColumn(SchemaConstants.DeletedAtColumn).ShouldBeTrue();
            }
        }
    }
}
