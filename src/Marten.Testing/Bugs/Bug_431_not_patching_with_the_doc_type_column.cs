using Marten.Schema;
using Marten.Testing.Documents;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_431_not_patching_with_the_doc_type_column
    {
        [Fact]
        public void should_add_a_missing_doc_type_column_in_patch()
        {
            using (var store1 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>();
            }))
            {
                store1.Advanced.Clean.CompletelyRemoveAll();

                store1.Schema.EnsureStorageExists(typeof(User));
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().AddSubClass<SuperUser>();
            }))
            {
                var patch = store2.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("alter table public.mt_doc_user add column mt_doc_type varchar DEFAULT \'BASE\'");
            }

        }
    }
}