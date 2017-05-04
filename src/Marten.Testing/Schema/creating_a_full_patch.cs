using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Schema
{
    public class creating_a_full_patch
    {
        [Fact]
        public void patch_for_multiple_tables()
        {
            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            {
                store.Advanced.Clean.CompletelyRemoveAll();

                store.Tenants.Default.EnsureStorageExists(typeof(User));
                store.Tenants.Default.EnsureStorageExists(typeof(Target));
                store.Tenants.Default.EnsureStorageExists(typeof(Issue));
                store.Tenants.Default.EnsureStorageExists(typeof(Company));
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.Schema.For<User>().Duplicate(x => x.UserName);
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
            }))
            {
                var patch = store2.Schema.ToPatch().UpdateDDL;

                // don't patch Target and Company because they don't change
                patch.ShouldNotContain("mt_doc_company");
                patch.ShouldNotContain("mt_doc_target");

                patch.ShouldContain("DROP FUNCTION public.mt_upsert_issue(doc jsonb, docdotnettype character varying, docid uuid, docversion uuid) cascade;");
                patch.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_issue(current_version uuid, doc JSONB, docDotNetType varchar, docId uuid, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");

                patch.ShouldContain("alter table public.mt_doc_user add column user_name varchar");
                patch.ShouldContain("update public.mt_doc_user set user_name = data ->> 'UserName';");
            }
        }

    }
}