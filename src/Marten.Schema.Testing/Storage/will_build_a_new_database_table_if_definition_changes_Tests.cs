using System.Linq;
using System.Threading.Tasks;
using Marten.Schema.Testing.Documents;
using Marten.Storage;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    [Collection("patching")]
    public class will_build_a_new_database_table_if_definition_changes_Tests : IntegrationContext
    {

        [Fact]
        public async Task will_build_the_new_table_if_the_configured_table_does_not_match_the_existing_table()
        {
            DocumentTable table1;
            DocumentTable table2;


            theStore.Tenancy.Default.StorageFor<User>();

            (await theStore.Tenancy.Default.DocumentTables()).Select(x => x.QualifiedName).ShouldContain($"public.mt_doc_user");

            table1 = theStore.TableSchema(typeof(User));
            table1.Columns.ShouldNotContain(x => x.Name == "user_name");

            var store = SeparateStore(opts => opts.Schema.For<User>().Duplicate(x => x.UserName));

            store.Tenancy.Default.StorageFor<User>();

            (await store.Tenancy.Default.DocumentTables()).Select(x => x.QualifiedName).ShouldContain($"public.mt_doc_user");

            table2 = store.TableSchema(typeof(User));

            table2.ShouldNotBe(table1);

            ShouldBeNullExtensions.ShouldNotBeNull(table2.ColumnFor("user_name"));
        }

    }
}
