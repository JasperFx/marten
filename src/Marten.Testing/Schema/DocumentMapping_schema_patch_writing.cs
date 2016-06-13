using Marten.Testing.Documents;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Schema
{
    public class DocumentMapping_schema_patch_writing : IntegratedFixture
    {
        private readonly ITestOutputHelper _output;

        public DocumentMapping_schema_patch_writing(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void creates_the_table_in_update_ddl_if_all_new()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("CREATE TABLE public.mt_doc_user");
            patch.UpdateDDL.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user(doc JSONB, docDotNetType varchar, docId uuid, docVersion uuid) RETURNS UUID LANGUAGE plpgsql AS $function$");
        }

        [Fact]
        public void drops_the_table_in_rollback_if_all_new()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists public.mt_doc_user cascade;");
        }

        [Fact]
        public void drops_the_table_in_rollback_if_all_new_different_schema()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
                _.DatabaseSchemaName = "other";
            });

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists other.mt_doc_user cascade;");
        }

        [Fact]
        public void does_not_drop_the_table_if_it_all_exists()
        {
            theStore.Schema.EnsureStorageExists(typeof(User));

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldNotContain("drop table if exists public.mt_doc_user cascade;");
        }

        [Fact]
        public void can_drop_added_columns_in_document_storage()
        {
            theStore.Schema.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.UserName);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.RollbackDDL.ShouldContain("alter table if exists public.mt_doc_user drop column if exists user_name;");
            }
        }
    }
}