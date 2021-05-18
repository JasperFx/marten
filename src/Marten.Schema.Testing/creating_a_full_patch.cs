using System.Threading.Tasks;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing
{
    [Collection("patching")]
    public class creating_a_full_patch : IntegrationContext
    {
        [Fact]
        public async Task patch_for_multiple_tables()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Issue));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Company));


            using (var store2 = SeparateStore(_ =>
            {
                _.Schema.For<User>().Duplicate(x => x.UserName);
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            }))
            {
                var patch = (await store2.Schema.CreateMigration()).UpdateSql;

                // don't patch Target and Company because they don't change
                patch.ShouldNotContain("mt_doc_company");
                patch.ShouldNotContain("mt_doc_target");

                patch.ShouldContain($"DROP FUNCTION IF EXISTS public.mt_upsert_issue(doc jsonb, docdotnettype character varying, docid uuid, docversion uuid) cascade;");
                patch.ShouldContain($"CREATE OR REPLACE FUNCTION public.mt_upsert_issue(current_version uuid, doc JSONB, docDotNetType varchar, docId uuid, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");

                patch.ShouldContain($"alter table public.mt_doc_user add column user_name varchar");
                patch.ShouldContain($"update public.mt_doc_user set user_name = data ->> 'UserName';");
            }
        }

        public creating_a_full_patch()
        {
        }
    }
}
