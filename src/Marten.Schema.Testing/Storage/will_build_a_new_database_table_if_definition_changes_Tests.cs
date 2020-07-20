using Marten.Schema.Testing.Documents;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    [Collection("patching")]
    public class will_build_a_new_database_table_if_definition_changes_Tests : IntegrationContext
    {

        [Fact]
        public void will_build_the_new_table_if_the_configured_table_does_not_match_the_existing_table()
        {
            DocumentTable table1;
            DocumentTable table2;


            theStore.Tenancy.Default.StorageFor<User>();

            theStore.Tenancy.Default.DbObjects.DocumentTables().ShouldContain($"public.mt_doc_user");

            table1 = theStore.TableSchema(typeof(User));
            table1.ShouldNotContain(x => x.Name == "user_name");

            var store = SeparateStore(opts => opts.Schema.For<User>().Duplicate(x => x.UserName));

            store.Tenancy.Default.StorageFor<User>();

            store.Tenancy.Default.DbObjects.DocumentTables().ShouldContain($"public.mt_doc_user");

            table2 = store.TableSchema(typeof(User));

            table2.ShouldNotBe(table1);

            ShouldBeNullExtensions.ShouldNotBeNull(table2.Column("user_name"));
        }

        [Fact]
        public void will_build_the_new_table_if_the_configured_table_does_not_match_the_existing_table_on_other_schema()
        {
            DocumentTable table1;
            DocumentTable table2;

            StoreOptions(_ => _.DatabaseSchemaName = "other");

            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            theStore.Tenancy.Default.DbObjects.DocumentTables().ShouldContain("other.mt_doc_user");

            table1 = theStore.TableSchema(typeof(User));
            table1.ShouldNotContain(x => x.Name == "user_name");


            var store2 = SeparateStore(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Duplicate(x => x.UserName);
                _.AutoCreateSchemaObjects = AutoCreate.All;
            });

            store2.Tenancy.Default.EnsureStorageExists(typeof(User));

            store2.Tenancy.Default.DbObjects.DocumentTables().ShouldContain("other.mt_doc_user");

            table2 = store2.TableSchema(typeof(User));


            table2.ShouldNotBe(table1);

            ShouldBeNullExtensions.ShouldNotBeNull(table2.Column("user_name"));
        }
    }
}
