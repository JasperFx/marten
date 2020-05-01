using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_431_not_patching_with_the_doc_type_column : BugIntegrationContext
    {
        [Fact]
        public void should_add_a_missing_doc_type_column_in_patch()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>();
            });


            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));


            var store2 = SeparateStore(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Schema.For<User>().AddSubClass<SuperUser>();
            });

            var patch = store2.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("alter table bugs.mt_doc_user add column mt_doc_type varchar DEFAULT \'BASE\'");
        }
    }
}
