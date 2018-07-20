using Marten.Schema;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentMapping_schema_patch_writing : IntegratedFixture
    {
        [Fact]
        public void creates_the_table_in_update_ddl_if_all_new()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("CREATE TABLE public.mt_doc_user");
            patch.UpdateDDL.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user(doc JSONB, docDotNetType varchar, docId uuid, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
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
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldNotContain("drop table if exists public.mt_doc_user cascade;");
        }

        [Fact]
        public void can_drop_added_columns_in_document_storage()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

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

        [Fact]
        public void can_drop_indexes_that_were_added()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Index(x => x.UserName);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.RollbackDDL.ShouldContain("drop index concurrently if exists public.mt_doc_user_idx_user_name;");
            }
        }

        [Fact]
        public void can_revert_indexes_that_changed()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.btree);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.hash);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.RollbackDDL.ShouldContain("drop index");
                
                patch.RollbackDDL.ShouldContain("CREATE INDEX mt_doc_user_idx_user_name");


            }
        }
        
        [Fact]
        public void can_revert_indexes_that_changed_in_non_public_schema()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.btree);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.hash);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.RollbackDDL.ShouldContain("drop index other.mt_doc_user_idx_user_name;");
                
                patch.RollbackDDL.ShouldContain("CREATE INDEX mt_doc_user_idx_user_name ON other.mt_doc_user USING btree (user_name);");


            }
        }
    }
}