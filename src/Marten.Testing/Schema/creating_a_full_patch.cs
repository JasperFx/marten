using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using IssueAssigned = Marten.Testing.Events.IssueAssigned;

namespace Marten.Testing.Schema
{
    public class creating_a_full_patch : DestructiveIntegrationContext
    {
        [Fact]
        public void patch_for_multiple_tables()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Issue));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Company));

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

        [Fact]
        public void base_patch_should_drop_system_functions_correctly()
        {

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventType(typeof(IssueAssigned));
            }))
            {
                var patch = store2.Schema.ToPatch();

                patch.RollbackDDL.ShouldContain("drop function if exists public.mt_immutable_timestamp(text) cascade;");
                patch.RollbackDDL.ShouldContain("drop function if exists public.mt_immutable_timestamptz(text) cascade;");
                patch.RollbackDDL.ShouldContain("DROP FUNCTION IF EXISTS public.mt_transform_patch_doc(JSONB, JSONB);");

                patch.RollbackDDL.ShouldContain("drop function if exists public.mt_append_event (uuid, varchar, varchar, uuid[], varchar[], jsonb[]);");
                patch.RollbackDDL.ShouldContain("drop function if exists public.mt_mark_event_progression(varchar, bigint) cascade;");
            }
        }

        public creating_a_full_patch(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
